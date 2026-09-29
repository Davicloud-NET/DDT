// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;
using Xunit;

namespace DDT.Protocols.Tests;

public sealed class ProxyDhcpResponderTests
{
    private static ProxyDhcpReply Respond(ProxyDhcpRequest request, ProxyDhcpConfiguration? configuration = null)
    {
        ProxyDhcpDecision decision = ProxyDhcpResponder.Respond(request, configuration ?? ProxyDhcpFixture.Default());

        Assert.True(decision.TryGetReply(out ProxyDhcpReply? reply), $"Expected a reply, got {decision.SilenceReason}.");

        return reply;
    }

    private static ProxyDhcpSilenceReason Silence(ProxyDhcpRequest request, ProxyDhcpConfiguration? configuration = null)
    {
        ProxyDhcpDecision decision = ProxyDhcpResponder.Respond(request, configuration ?? ProxyDhcpFixture.Default());

        Assert.False(decision.TryGetReply(out _), "Expected silence, got a reply.");

        return decision.SilenceReason;
    }

    [Fact]
    public void OffersTheConfiguredBootFileToAUefiClient()
    {
        ProxyDhcpReply reply = Respond(ProxyDhcpFixture.Request("discover-uefi-x64-pxeclient"));

        Assert.Equal(DhcpMessageType.Offer, reply.MessageType);
        Assert.Equal("ddt/x64/wdsmgfw.efi", reply.BootFileName);
        Assert.Equal(ProxyDhcpFixture.ServerAddress, reply.NextServerAddress);
        Assert.Equal(ProxyDhcpFixture.ServerAddress, reply.ServerIdentifier);
        Assert.Equal(0x11223344u, reply.TransactionId);
    }

    [Fact]
    public void EchoesTheShortVendorClassAndTheMachineIdentifierVerbatim()
    {
        ProxyDhcpRequest request = ProxyDhcpFixture.Request("discover-uefi-x64-pxeclient");
        ProxyDhcpReply reply = Respond(request);

        Assert.Equal("PXEClient", reply.VendorClassIdentifier);
        Assert.Equal(request.Message.MachineIdentifier.ToArray(), reply.MachineIdentifier.ToArray());
    }

    [Fact]
    public void SendsOption43OnlyToLegacyBiosClients()
    {
        // UEFI firmware classifies an offer carrying option 43 differently and can refuse it.
        Assert.True(Respond(ProxyDhcpFixture.Request("discover-uefi-x64-pxeclient")).VendorSpecific.IsEmpty);

        ReadOnlyMemory<byte> bios = Respond(ProxyDhcpFixture.Request("discover-bios-x86")).VendorSpecific;

        // Sub-option 6 (PXE_DISCOVERY_CONTROL) length 1 value 8, then the End sub-option.
        Assert.Equal(new byte[] { 6, 1, 8, 255 }, bios.ToArray());
    }

    [Fact]
    public void AnswersAnHttpBootClientWithAUrlAndNoNextServer()
    {
        ProxyDhcpReply reply = Respond(ProxyDhcpFixture.Request("discover-httpclient-x64"));

        Assert.Equal("HTTPClient", reply.VendorClassIdentifier);
        Assert.Equal("http://192.0.2.10:8080/boot/x64/bootmgfw.efi", reply.BootFileName);
        Assert.Equal(IPAddress.Any, reply.NextServerAddress);
    }

    [Fact]
    public void BroadcastsAnOfferMadeOnPort67()
    {
        ProxyDhcpReply reply = Respond(ProxyDhcpFixture.Request("discover-uefi-x64-pxeclient"));

        Assert.Equal(IPAddress.Broadcast, reply.Destination.Address);
        Assert.Equal(68, reply.Destination.Port);

        // A redirection service offers no address, so yiaddr stays zero and a unicast reply to it is
        // impossible. Forcing the flag is a deliberate deviation from RFC 1542 section 5.4.
        Assert.Equal(0x8000, reply.Flags & 0x8000);
    }

    [Fact]
    public void UnicastsBackToTheClientSourcePortOnPort4011()
    {
        ProxyDhcpRequest request = ProxyDhcpFixture.Request(
            "request-4011-uefi-x64",
            ProxyDhcpListenPort.PxeBootServer,
            IPAddress.Parse("192.0.2.55"),
            sourcePort: 2070);

        ProxyDhcpReply reply = Respond(request);

        Assert.Equal(DhcpMessageType.Ack, reply.MessageType);
        Assert.Equal(IPAddress.Parse("192.0.2.55"), reply.Destination.Address);
        Assert.Equal(2070, reply.Destination.Port);

        // The client already has an address here, so the broadcast flag must not be forced on.
        Assert.Equal(0, reply.Flags & 0x8000);
    }

    [Fact]
    public void SendsARelayedReplyToTheRelayAgentOnPort67()
    {
        DhcpMessage relayed = ProxyDhcpFixture.Message("discover-uefi-x64-pxeclient") with
        {
            GatewayAddress = ProxyDhcpFixture.RelayAddress,
        };

        ProxyDhcpConfiguration configuration = ProxyDhcpFixture.Default() with
        {
            AuthorisedRelayAgents = [ProxyDhcpFixture.RelayAddress],
        };

        ProxyDhcpRequest request = new()
        {
            Message = relayed,
            ReceivedOn = ProxyDhcpListenPort.Dhcp,
            LocalAddress = ProxyDhcpFixture.ServerAddress,
            SourceAddress = ProxyDhcpFixture.RelayAddress,
            SourcePort = 67,
        };

        ProxyDhcpReply reply = Respond(request, configuration);

        Assert.Equal(ProxyDhcpFixture.RelayAddress, reply.Destination.Address);
        Assert.Equal(67, reply.Destination.Port);
    }

    [Fact]
    public void StaysSilentForAnUnauthorisedRelayAgent()
    {
        DhcpMessage relayed = ProxyDhcpFixture.Message("discover-uefi-x64-pxeclient") with
        {
            GatewayAddress = ProxyDhcpFixture.RelayAddress,
        };

        ProxyDhcpRequest request = new()
        {
            Message = relayed,
            ReceivedOn = ProxyDhcpListenPort.Dhcp,
            LocalAddress = ProxyDhcpFixture.ServerAddress,
            SourceAddress = ProxyDhcpFixture.RelayAddress,
            SourcePort = 67,
        };

        // Answering an ip-helper DDT was never configured for means serving an arbitrary remote
        // segment a boot target chosen with no site context.
        Assert.Equal(ProxyDhcpSilenceReason.RelayNotAuthorised, Silence(request));
    }

    [Fact]
    public void StaysSilentForAnOrdinaryDhcpClient()
    {
        Assert.Equal(
            ProxyDhcpSilenceReason.NoVendorClass,
            Silence(ProxyDhcpFixture.Request("discover-no-vendor-class")));
    }

    [Fact]
    public void StaysSilentForAnUnrecognisedVendorClass()
    {
        DhcpMessage message = ProxyDhcpFixture.Message("discover-uefi-x64-pxeclient") with
        {
            VendorClassIdentifier = "MSFT 5.0",
        };

        ProxyDhcpRequest request = new()
        {
            Message = message,
            ReceivedOn = ProxyDhcpListenPort.Dhcp,
            LocalAddress = ProxyDhcpFixture.ServerAddress,
            SourceAddress = IPAddress.Any,
            SourcePort = 68,
        };

        Assert.Equal(ProxyDhcpSilenceReason.UnrecognisedVendorClass, Silence(request));
    }

    [Fact]
    public void StaysSilentForAMalformedArchitecture()
    {
        Assert.Equal(
            ProxyDhcpSilenceReason.MalformedClientArchitecture,
            Silence(ProxyDhcpFixture.Request("discover-odd-length-architecture")));
    }

    [Fact]
    public void StaysSilentWhenNoBootTargetIsConfiguredForTheArchitecture()
    {
        ProxyDhcpConfiguration empty = ProxyDhcpFixture.Configuration(new BootTarget
        {
            Architecture = ClientArchitecture.X86Bios,
            Method = BootMethod.Tftp,
            BootFile = "ddt/x86/pxelinux.0",
        });

        Assert.Equal(
            ProxyDhcpSilenceReason.NoBootTargetForArchitecture,
            Silence(ProxyDhcpFixture.Request("discover-uefi-x64-pxeclient"), empty));
    }

    [Fact]
    public void StaysSilentWhenAPxeClientWouldBeHandedAUrl()
    {
        ProxyDhcpConfiguration httpOnly = ProxyDhcpFixture.Configuration(new BootTarget
        {
            Architecture = ClientArchitecture.X64Uefi,
            Method = BootMethod.Http,
            BootFile = "http://192.0.2.10:8080/boot/x64/bootmgfw.efi",
        });

        // A PXE client cannot fetch a URL. Handing it one produces a machine that sits at a blank
        // screen until it times out, which is worse than not answering.
        Assert.Equal(
            ProxyDhcpSilenceReason.BootMethodDoesNotMatchVendorClass,
            Silence(ProxyDhcpFixture.Request("discover-uefi-x64-pxeclient"), httpOnly));
    }

    [Fact]
    public void StaysSilentWhenTheRequestNamesAnotherServer()
    {
        DhcpMessage message = ProxyDhcpFixture.Message("request-4011-uefi-x64") with
        {
            ServerIdentifier = IPAddress.Parse("192.0.2.200"),
        };

        ProxyDhcpRequest request = new()
        {
            Message = message,
            ReceivedOn = ProxyDhcpListenPort.PxeBootServer,
            LocalAddress = ProxyDhcpFixture.ServerAddress,
            SourceAddress = IPAddress.Parse("192.0.2.55"),
            SourcePort = 2070,
        };

        Assert.Equal(ProxyDhcpSilenceReason.ServerIdentifierNamesAnotherServer, Silence(request));
    }

    [Theory]
    [InlineData("discover-uefi-x64-pxeclient", ProxyDhcpListenPort.PxeBootServer)]
    [InlineData("request-4011-uefi-x64", ProxyDhcpListenPort.Dhcp)]
    public void StaysSilentWhenTheMessageTypeDoesNotBelongOnThatPort(string fixture, ProxyDhcpListenPort port)
    {
        Assert.Equal(
            ProxyDhcpSilenceReason.MessageTypeNotHandledOnPort,
            Silence(ProxyDhcpFixture.Request(fixture, port)));
    }

    [Fact]
    public void StaysSilentForABootReply()
    {
        DhcpMessage reply = ProxyDhcpFixture.Message("discover-uefi-x64-pxeclient") with
        {
            Operation = DhcpOperation.BootReply,
        };

        ProxyDhcpRequest request = new()
        {
            Message = reply,
            ReceivedOn = ProxyDhcpListenPort.Dhcp,
            LocalAddress = ProxyDhcpFixture.ServerAddress,
            SourceAddress = IPAddress.Any,
            SourcePort = 68,
        };

        // Its own broadcast reply can arrive back on the socket it was sent from.
        Assert.Equal(ProxyDhcpSilenceReason.NotABootRequest, Silence(request));
    }
}

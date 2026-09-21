// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Protocols.Dhcp;

namespace DDT.Protocols.Pxe;

public static class ProxyDhcpResponder
{
    // PXE 2.1 section 2.5.1.1: a redirection service offers no address, so yiaddr stays zero and
    // the reply can never be unicast to it. That is what justifies forcing the broadcast flag
    // below, against the default rule in RFC 1542 section 5.4.
    private const ushort BroadcastFlag = 0x8000;

    // Option 43 sub-option 6 (PXE_DISCOVERY_CONTROL) with bit 3 set tells the client to skip
    // multicast and broadcast discovery and boot the file named in this reply.
    private static ReadOnlySpan<byte> SkipDiscoveryControl => [6, 1, 8, 255];

    public static ProxyDhcpDecision Respond(ProxyDhcpRequest request, ProxyDhcpConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(configuration);

        DhcpMessage message = request.Message;

        if (message.Operation != DhcpOperation.BootRequest)
        {
            return ProxyDhcpDecision.Silent(ProxyDhcpSilenceReason.NotABootRequest);
        }

        if (!IsHandledOnPort(message.MessageType, request.ReceivedOn))
        {
            return ProxyDhcpDecision.Silent(ProxyDhcpSilenceReason.MessageTypeNotHandledOnPort);
        }

        if (string.IsNullOrEmpty(message.VendorClassIdentifier))
        {
            return ProxyDhcpDecision.Silent(ProxyDhcpSilenceReason.NoVendorClass);
        }

        if (!PxeVendorClass.TryMatch(message.VendorClassIdentifier, out string family))
        {
            return ProxyDhcpDecision.Silent(ProxyDhcpSilenceReason.UnrecognisedVendorClass);
        }

        if (message.ArchitectureMalformed)
        {
            return ProxyDhcpDecision.Silent(ProxyDhcpSilenceReason.MalformedClientArchitecture);
        }

        if (message.Architecture is not { } architecture)
        {
            return ProxyDhcpDecision.Silent(ProxyDhcpSilenceReason.NoClientArchitecture);
        }

        if (!configuration.BootTargets.TryGetValue(architecture, out BootTarget? target))
        {
            return ProxyDhcpDecision.Silent(ProxyDhcpSilenceReason.NoBootTargetForArchitecture);
        }

        // A PXE client must never be handed a URL, and an HTTP Boot client must never be handed a
        // TFTP path. Both produce a machine that sits at a blank screen until it times out.
        string expectedFamily = target.Method == BootMethod.Http ? PxeVendorClass.Http : PxeVendorClass.Pxe;

        if (!string.Equals(family, expectedFamily, StringComparison.Ordinal))
        {
            return ProxyDhcpDecision.Silent(ProxyDhcpSilenceReason.BootMethodDoesNotMatchVendorClass);
        }

        if (message.ServerIdentifier is { } requested && !IsOurs(requested, configuration, request.LocalAddress))
        {
            return ProxyDhcpDecision.Silent(ProxyDhcpSilenceReason.ServerIdentifierNamesAnotherServer);
        }

        bool relayed = !message.GatewayAddress.Equals(IPAddress.Any);

        if (relayed && !configuration.AuthorisedRelayAgents.Contains(message.GatewayAddress))
        {
            return ProxyDhcpDecision.Silent(ProxyDhcpSilenceReason.RelayNotAuthorised);
        }

        return ProxyDhcpDecision.Respond(BuildReply(request, target, relayed, family));
    }

    private static ProxyDhcpReply BuildReply(
        ProxyDhcpRequest request,
        BootTarget target,
        bool relayed,
        string family)
    {
        DhcpMessage message = request.Message;
        IPAddress serverAddress = target.ServerAddress ?? request.LocalAddress;
        bool onBootServerPort = request.ReceivedOn == ProxyDhcpListenPort.PxeBootServer;

        DhcpReplyDestination destination = relayed
            ? new DhcpReplyDestination(message.GatewayAddress, (int)ProxyDhcpListenPort.Dhcp)
            : onBootServerPort
                ? new DhcpReplyDestination(
                    message.ClientAddress.Equals(IPAddress.Any) ? request.SourceAddress : message.ClientAddress,
                    request.SourcePort)
                : new DhcpReplyDestination(IPAddress.Broadcast, 68);

        return new ProxyDhcpReply
        {
            MessageType = message.MessageType == DhcpMessageType.Discover
                ? DhcpMessageType.Offer
                : DhcpMessageType.Ack,
            TransactionId = message.TransactionId,
            Flags = onBootServerPort && !relayed ? message.Flags : (ushort)(message.Flags | BroadcastFlag),
            HardwareType = message.HardwareType,
            ClientHardwareAddress = message.ClientHardwareAddress,
            ClientAddress = message.ClientAddress,
            GatewayAddress = message.GatewayAddress,
            NextServerAddress = target.Method == BootMethod.Http ? IPAddress.Any : serverAddress,
            ServerIdentifier = serverAddress,
            ServerHostName = target.ServerHostName ?? serverAddress.ToString(),
            BootFileName = target.BootFile,
            VendorClassIdentifier = family,
            MachineIdentifier = message.MachineIdentifier,
            VendorSpecific = target.AdvertiseBootServerDiscovery
                ? SkipDiscoveryControl.ToArray()
                : ReadOnlyMemory<byte>.Empty,
            Destination = destination,
        };
    }

    // Port 67 carries the initial broadcast discovery. Port 4011 is where firmware comes back once
    // it has an address and wants the boot file, and it uses REQUEST or INFORM depending on vendor.
    private static bool IsHandledOnPort(DhcpMessageType messageType, ProxyDhcpListenPort port) =>
        port switch
        {
            ProxyDhcpListenPort.Dhcp => messageType == DhcpMessageType.Discover,
            ProxyDhcpListenPort.PxeBootServer =>
                messageType is DhcpMessageType.Request or DhcpMessageType.Inform,
            _ => false,
        };

    private static bool IsOurs(IPAddress requested, ProxyDhcpConfiguration configuration, IPAddress localAddress) =>
        requested.Equals(localAddress) || configuration.LocalAddresses.Contains(requested);
}

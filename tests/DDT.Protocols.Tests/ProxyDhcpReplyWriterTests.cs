using System.Buffers.Binary;
using System.Net;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;
using Xunit;

namespace DDT.Protocols.Tests;

public sealed class ProxyDhcpReplyWriterTests
{
    private static byte[] Write(string fixture, ProxyDhcpListenPort port = ProxyDhcpListenPort.Dhcp)
    {
        ProxyDhcpDecision decision = ProxyDhcpResponder.Respond(
            ProxyDhcpFixture.Request(fixture, port, IPAddress.Parse("192.0.2.55"), 2070),
            ProxyDhcpFixture.Default());

        Assert.True(decision.TryGetReply(out ProxyDhcpReply? reply));

        byte[] buffer = new byte[1500];
        Assert.True(ProxyDhcpReplyWriter.TryWrite(reply, buffer, out int written));

        return buffer[..written];
    }

    [Fact]
    public void WritesABootReplyHeaderThatMatchesTheRequest()
    {
        byte[] datagram = Write("discover-uefi-x64-pxeclient");

        Assert.Equal(DhcpOperation.BootReply, datagram[0]);
        Assert.Equal(1, datagram[1]);
        Assert.Equal(6, datagram[2]);
        Assert.Equal(0, datagram[3]);
        Assert.Equal(0x11223344u, BinaryPrimitives.ReadUInt32BigEndian(datagram.AsSpan(4)));

        // RFC 2131 Table 3: secs and hops are zero in an OFFER.
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(datagram.AsSpan(8)));
    }

    [Fact]
    public void NeverOffersAnAddress()
    {
        byte[] datagram = Write("discover-uefi-x64-pxeclient");

        // PXE 2.1 section 2.5.1.1: a redirection service assigns no address, so yiaddr must be zero.
        Assert.Equal(new byte[4], datagram[16..20]);
    }

    [Fact]
    public void CarriesTheMagicCookieAtTheDocumentedOffset()
    {
        Assert.Equal(
            new byte[] { 0x63, 0x82, 0x53, 0x63 },
            Write("discover-uefi-x64-pxeclient")[236..240]);
    }

    [Fact]
    public void PadsToTheMinimumBootpDatagramLength()
    {
        // RFC 1542 section 2.1: a BOOTP datagram must be able to hold the minimal 300 octet message.
        Assert.True(Write("discover-uefi-x64-pxeclient").Length >= 300);
    }

    [Fact]
    public void RoundTripsThroughTheParser()
    {
        byte[] datagram = Write("discover-uefi-x64-pxeclient");

        Assert.True(DhcpMessageParser.TryParse(datagram, out DhcpMessage? parsed, out DhcpParseError error), $"{error}");
        Assert.Equal(DhcpMessageType.Offer, parsed.MessageType);
        Assert.Equal("PXEClient", parsed.VendorClassIdentifier);
        Assert.Equal(ProxyDhcpFixture.ServerAddress, parsed.ServerIdentifier);
        Assert.Equal(0x11223344u, parsed.TransactionId);
        Assert.Equal(17, parsed.MachineIdentifier.Length);
    }

    [Fact]
    public void WritesTheBootFileIntoBothTheFixedFieldAndOption67()
    {
        byte[] datagram = Write("discover-uefi-x64-pxeclient");

        string fixedField = System.Text.Encoding.ASCII.GetString(datagram[108..236]).TrimEnd('\0');
        Assert.Equal("ddt/x64/wdsmgfw.efi", fixedField);

        DhcpOptionReader reader = new(datagram.AsSpan(240));
        string? option67 = null;

        while (reader.MoveNext())
        {
            if (reader.Code == DhcpOption.BootFileName)
            {
                option67 = System.Text.Encoding.ASCII.GetString(reader.Value);
            }
        }

        // Some firmware reads only one of the two and they disagree about which.
        Assert.Equal("ddt/x64/wdsmgfw.efi", option67);
    }

    [Fact]
    public void RefusesABufferSmallerThanTheMinimumDatagram()
    {
        ProxyDhcpDecision decision = ProxyDhcpResponder.Respond(
            ProxyDhcpFixture.Request("discover-uefi-x64-pxeclient"),
            ProxyDhcpFixture.Default());

        Assert.True(decision.TryGetReply(out ProxyDhcpReply? reply));
        Assert.False(ProxyDhcpReplyWriter.TryWrite(reply, new byte[299], out int written));
        Assert.Equal(0, written);
    }

    [Fact]
    public void LeavesTheFixedFieldEmptyWhenTheBootFileNameDoesNotFit()
    {
        ProxyDhcpDecision decision = ProxyDhcpResponder.Respond(
            ProxyDhcpFixture.Request("discover-uefi-x64-pxeclient"),
            ProxyDhcpFixture.Configuration(new BootTarget
            {
                Architecture = ClientArchitecture.X64Uefi,
                Method = BootMethod.Tftp,
                BootFile = new string('a', 200),
            }));

        Assert.True(decision.TryGetReply(out ProxyDhcpReply? reply));

        byte[] buffer = new byte[1500];
        Assert.True(ProxyDhcpReplyWriter.TryWrite(reply, buffer, out int written));

        // A truncated unterminated path would send firmware chasing a file that does not exist.
        // Option 67 has no length limit and carries the real value.
        Assert.Equal(new byte[128], buffer[108..236]);

        DhcpOptionReader reader = new(buffer.AsSpan(240, written - 240));
        string? option67 = null;

        while (reader.MoveNext())
        {
            if (reader.Code == DhcpOption.BootFileName)
            {
                option67 = System.Text.Encoding.ASCII.GetString(reader.Value);
            }
        }

        Assert.Equal(new string('a', 200), option67);
    }
}

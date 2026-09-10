using System.Net;
using DDT.Protocols.Dhcp;
using Xunit;

namespace DDT.Protocols.Tests;

public sealed class DhcpMessageParserTests
{
    private static DhcpMessage Parse(string name)
    {
        Assert.True(
            DhcpMessageParser.TryParse(PacketFixture.Load("Dhcp", name), out DhcpMessage? message, out DhcpParseError error),
            $"{name} failed to parse: {error}");

        return message;
    }

    [Fact]
    public void ReadsTheFixedHeaderOfAUefiDiscover()
    {
        DhcpMessage message = Parse("discover-uefi-x64-pxeclient");

        Assert.Equal(DhcpOperation.BootRequest, message.Operation);
        Assert.Equal(1u, message.HardwareType);
        Assert.Equal(0x11223344u, message.TransactionId);
        Assert.Equal(4, message.Seconds);
        Assert.True(message.IsBroadcastRequested);
        Assert.Equal(IPAddress.Any, message.ClientAddress);
        Assert.Equal(IPAddress.Any, message.GatewayAddress);
        Assert.Equal(new byte[] { 0x00, 0x15, 0x5d, 0x01, 0x02, 0x03 }, message.ClientHardwareAddress.ToArray());
    }

    [Fact]
    public void ReadsThePxeOptionsOfAUefiDiscover()
    {
        DhcpMessage message = Parse("discover-uefi-x64-pxeclient");

        Assert.Equal(DhcpMessageType.Discover, message.MessageType);
        Assert.Equal(ClientArchitecture.X64Uefi, message.Architecture);
        Assert.False(message.ArchitectureMalformed);
        Assert.Equal("PXEClient:Arch:00007:UNDI:003001", message.VendorClassIdentifier);
        Assert.Equal((ushort)1472, message.MaximumMessageSize);
        Assert.True(message.RequestsOption(DhcpOption.BootFileName));
    }

    [Fact]
    public void KeepsAllSeventeenOctetsOfOption97()
    {
        DhcpMessage message = Parse("discover-uefi-x64-pxeclient");

        // The type octet is kept so the option can be echoed back byte for byte. DDT never renders
        // these as a Guid, because the byte order of the first three fields is disputed.
        Assert.Equal(17, message.MachineIdentifier.Length);
        Assert.Equal(0x00, message.MachineIdentifier.Span[0]);
        Assert.Equal(
            Convert.FromHexString("44454c4c570010388036b7c04f5a344a"),
            message.MachineIdentifier[1..].ToArray());
    }

    [Fact]
    public void ReadsOptionsOutOfTheFileAndServerNameFieldsWhenOverloaded()
    {
        DhcpMessage message = Parse("discover-option-overload-both");

        // Option 60 lives in the file field and option 55 in the sname field. Finding both proves
        // the RFC 2131 section 4.1 parse order was followed.
        Assert.Equal("PXEClient:Arch:00007:UNDI:003001", message.VendorClassIdentifier);
        Assert.True(message.RequestsOption(DhcpOption.BootFileName));
        Assert.Equal(ClientArchitecture.X64Uefi, message.Architecture);
    }

    [Fact]
    public void RejectsAnOptionThatOverrunsTheDatagram()
    {
        Assert.False(DhcpMessageParser.TryParse(
            PacketFixture.Load("Dhcp", "discover-option-length-overruns"),
            out DhcpMessage? message,
            out DhcpParseError error));

        Assert.Null(message);
        Assert.Equal(DhcpParseError.OptionOverrunsDatagram, error);
    }

    [Fact]
    public void FlagsAnOddLengthArchitectureWithoutGuessingAValue()
    {
        DhcpMessage message = Parse("discover-odd-length-architecture");

        Assert.True(message.ArchitectureMalformed);
        Assert.Null(message.Architecture);
    }

    [Theory]
    [InlineData("discover-bios-x86", ClientArchitecture.X86Bios)]
    [InlineData("discover-uefi-x64-pxeclient", ClientArchitecture.X64Uefi)]
    [InlineData("discover-httpclient-x64", ClientArchitecture.X64UefiHttp)]
    public void ReadsTheArchitectureOfEachClientKind(string fixture, ClientArchitecture expected)
    {
        Assert.Equal(expected, Parse(fixture).Architecture);
    }

    [Fact]
    public void RejectsADatagramShorterThanTheFixedHeaderAndCookie()
    {
        Assert.False(DhcpMessageParser.TryParse(new byte[239], out _, out DhcpParseError error));
        Assert.Equal(DhcpParseError.TooShort, error);
    }

    [Fact]
    public void RejectsADatagramWithoutTheMagicCookie()
    {
        byte[] datagram = PacketFixture.Load("Dhcp", "discover-uefi-x64-pxeclient");
        datagram[236] ^= 0xFF;

        Assert.False(DhcpMessageParser.TryParse(datagram, out _, out DhcpParseError error));
        Assert.Equal(DhcpParseError.BadMagicCookie, error);
    }

    [Fact]
    public void RejectsADatagramWithNoMessageType()
    {
        byte[] datagram = PacketFixture.Load("Dhcp", "discover-uefi-x64-pxeclient");
        datagram[240] = DhcpOption.Pad;
        datagram[241] = DhcpOption.Pad;
        datagram[242] = DhcpOption.Pad;

        Assert.False(DhcpMessageParser.TryParse(datagram, out _, out DhcpParseError error));
        Assert.Equal(DhcpParseError.MissingMessageType, error);
    }

    [Fact]
    public void NeverThrowsOnTruncationAtAnyLength()
    {
        byte[] datagram = PacketFixture.Load("Dhcp", "discover-uefi-x64-pxeclient");

        // Anyone on the segment can send a short datagram, and an exception here would take out the
        // socket loop rather than one packet.
        for (int length = 0; length < datagram.Length; length++)
        {
            _ = DhcpMessageParser.TryParse(datagram.AsSpan(0, length), out _, out _);
        }
    }

    [Fact]
    public void NeverThrowsOnCorruptedOptionBytes()
    {
        byte[] original = PacketFixture.Load("Dhcp", "discover-uefi-x64-pxeclient");

        for (int index = 240; index < original.Length; index++)
        {
            byte[] datagram = [.. original];
            datagram[index] ^= 0xFF;

            _ = DhcpMessageParser.TryParse(datagram, out _, out _);
        }
    }
}

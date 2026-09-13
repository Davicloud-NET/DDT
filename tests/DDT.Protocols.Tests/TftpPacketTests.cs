using DDT.Protocols.Tftp;
using Xunit;

namespace DDT.Protocols.Tests;

public sealed class TftpPacketTests
{
    [Fact]
    public void ReadsAReadRequestWithNegotiatedOptions()
    {
        Assert.True(TftpPacket.TryReadReadRequest(
            PacketFixture.Load("Tftp", "rrq-wdsmgfw-blksize1456-window4"),
            out TftpReadRequest? request));

        Assert.Equal("ddt/x64/wdsmgfw.efi", request.FileName);
        Assert.Equal("octet", request.Mode);
        Assert.Equal(1456, request.Options.BlockSize);
        Assert.Equal(0, request.Options.TransferSize);
        Assert.Equal(4, request.Options.WindowSize);
        Assert.Null(request.Options.Timeout);
    }

    [Fact]
    public void ReadsAReadRequestWithNoOptions()
    {
        Assert.True(TftpPacket.TryReadReadRequest(
            PacketFixture.Load("Tftp", "rrq-wdsmgfw-no-options"),
            out TftpReadRequest? request));

        Assert.False(request.Options.Any);
    }

    [Fact]
    public void ReadsAnAcknowledgement()
    {
        Assert.True(TftpPacket.TryReadAcknowledgement(PacketFixture.Load("Tftp", "ack-block0"), out ushort block));
        Assert.Equal(0, block);
    }

    [Fact]
    public void ReadsTheOpcodeOfAnErrorPacket()
    {
        Assert.True(TftpPacket.TryReadOpcode(PacketFixture.Load("Tftp", "error-8-option-negotiation"), out TftpOpcode opcode));
        Assert.Equal(TftpOpcode.Error, opcode);
    }

    [Fact]
    public void RejectsAnAcknowledgementOfTheWrongLength()
    {
        Assert.False(TftpPacket.TryReadAcknowledgement([0x00, 0x04, 0x00], out _));
        Assert.False(TftpPacket.TryReadAcknowledgement([0x00, 0x04, 0x00, 0x01, 0x00], out _));
    }

    [Fact]
    public void RejectsARequestWithNoTerminator()
    {
        // A file name that runs to the end of the datagram has no terminator, and reading past it is
        // exactly the bug this reader exists to prevent.
        Assert.False(TftpPacket.TryReadReadRequest([0x00, 0x01, (byte)'a', (byte)'b'], out _));
    }

    [Fact]
    public void WritesAnOptionAckContainingOnlyAcknowledgedOptions()
    {
        TftpNegotiation negotiation = new()
        {
            BlockSize = 1380,
            WindowSize = 4,
            Timeout = TimeSpan.FromSeconds(1),
            TransferSize = 123456,
            AcknowledgeBlockSize = true,
            AcknowledgeWindowSize = true,
            AcknowledgeTransferSize = true,
            AcknowledgeTimeout = false,
        };

        byte[] buffer = new byte[128];
        Assert.True(TftpPacket.TryWriteOptionAck(buffer, negotiation, out int written));

        string text = System.Text.Encoding.ASCII.GetString(buffer[2..written]);

        Assert.Contains("blksize\u00001380\u0000", text, StringComparison.Ordinal);
        Assert.Contains("tsize\u0000123456\u0000", text, StringComparison.Ordinal);
        Assert.Contains("windowsize\u00004\u0000", text, StringComparison.Ordinal);
        Assert.DoesNotContain("timeout", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NeverThrowsOnTruncatedRequests()
    {
        byte[] request = PacketFixture.Load("Tftp", "rrq-wdsmgfw-blksize1456-window4");

        for (int length = 0; length < request.Length; length++)
        {
            _ = TftpPacket.TryReadReadRequest(request.AsSpan(0, length), out _);
        }
    }
}

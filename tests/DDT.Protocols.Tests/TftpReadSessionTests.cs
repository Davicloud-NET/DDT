using System.Buffers.Binary;
using DDT.Protocols.Tftp;
using Xunit;

namespace DDT.Protocols.Tests;

public sealed class TftpReadSessionTests
{
    private static readonly DateTimeOffset s_start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TftpReadRequest Request(string fixture)
    {
        Assert.True(TftpPacket.TryReadReadRequest(PacketFixture.Load("Tftp", fixture), out TftpReadRequest? request));

        return request;
    }

    private static byte[] Ack(ushort block)
    {
        byte[] datagram = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(datagram, (ushort)TftpOpcode.Acknowledgement);
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(2), block);

        return datagram;
    }

    private static TftpReadSession Session(
        TftpReadRequest request,
        long fileLength,
        TestTimeProvider clock,
        TftpLimits? limits = null) =>
        new(request, fileLength, limits ?? TftpLimits.Default, clock);

    private static List<TftpSendData> DataOf(TftpStep step) => [.. step.Actions.OfType<TftpSendData>()];

    [Fact]
    public void NegotiatesTheBlockSizeDownToTheServerLimit()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-blksize1456-window4"), 1_000_000, clock);

        // RFC 2348 allows a server to negotiate only downwards. The client asked for 1456.
        Assert.Equal(1400, session.Negotiated.BlockSize);
        Assert.True(session.Negotiated.AcknowledgeBlockSize);
    }

    [Fact]
    public void LeavesAWindowSizeAtOrBelowTheLimitAlone()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-blksize1456-window4"), 1_000_000, clock);

        Assert.Equal(4, session.Negotiated.WindowSize);
        Assert.True(session.Negotiated.AcknowledgeWindowSize);
    }

    [Fact]
    public void ReportsTheRealFileLengthForATransferSizeRequestOfZero()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-blksize1456-window4"), 1_000_000, clock);

        Assert.Equal(1_000_000, session.Negotiated.TransferSize);
    }

    [Fact]
    public void SendsNoOptionAckWhenTheClientAskedForNothing()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-no-options"), 10_000, clock);

        TftpStep step = session.Start();

        Assert.False(session.Negotiated.AnyAcknowledged);
        Assert.Equal(TftpSessionState.Transferring, session.State);
        Assert.Empty(step.Actions.OfType<TftpSendOptionAck>());

        // RFC 1350: without options the transfer is 512 octet blocks starting at block 1.
        TftpSendData first = Assert.Single(DataOf(step));
        Assert.Equal(1, first.Block);
        Assert.Equal(0, first.FileOffset);
        Assert.Equal(512, first.Length);
    }

    [Fact]
    public void OmitsAnOutOfRangeTimeoutFromTheOptionAckRatherThanClampingIt()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-timeout-out-of-range"), 10_000, clock);

        // RFC 2349 requires the acknowledged value to equal the requested one. Acknowledging a
        // clamped value is a malformed OACK, which EDK2 answers with ERROR 4 and then gives up.
        Assert.False(session.Negotiated.AcknowledgeTimeout);
        Assert.False(session.Negotiated.AnyAcknowledged);
    }

    [Fact]
    public void WaitsForTheClientToConfirmTheOptionAckBeforeSendingData()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-blksize1456-window4"), 1_000_000, clock);

        TftpStep opening = session.Start();

        Assert.Equal(TftpSessionState.AwaitingOptionAck, session.State);
        Assert.Single(opening.Actions.OfType<TftpSendOptionAck>());
        Assert.Empty(DataOf(opening));

        TftpStep afterAck = session.OnDatagram(Ack(0));

        Assert.Equal(TftpSessionState.Transferring, session.State);
        Assert.Equal(4, DataOf(afterAck).Count);
    }

    [Fact]
    public void FillsTheWholeWindowAndArmsOneTimer()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-blksize1456-window4"), 1_000_000, clock);

        session.Start();
        TftpStep step = session.OnDatagram(Ack(0));

        List<TftpSendData> data = DataOf(step);

        Assert.Equal([1, 2, 3, 4], data.Select(d => (int)d.Block));
        Assert.Equal([0, 1400, 2800, 4200], data.Select(d => d.FileOffset));
        Assert.Single(step.Actions.OfType<TftpArmRetransmit>());
    }

    [Fact]
    public void ResumesTheWindowFromTheLastAcknowledgedBlock()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-blksize1456-window4"), 1_000_000, clock);

        session.Start();
        session.OnDatagram(Ack(0));

        // RFC 7440 section 4: the last received acknowledgement sets the start of the next window.
        // Acknowledging block 2 rather than 4 means blocks 3 and 4 are sent again.
        TftpStep step = session.OnDatagram(Ack(2));

        Assert.Equal([3, 4, 5, 6], DataOf(step).Select(d => (int)d.Block));
    }

    [Fact]
    public void IgnoresADuplicateAcknowledgement()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-blksize1456-window4"), 1_000_000, clock);

        session.Start();
        session.OnDatagram(Ack(0));
        session.OnDatagram(Ack(4));

        TftpStep duplicate = session.OnDatagram(Ack(4));

        // RFC 1123 section 4.2.3.1: answering a duplicate acknowledgement restarts the window and
        // produces the Sorcerer's Apprentice packet storm.
        Assert.Empty(duplicate.Actions);
        Assert.Equal(4, session.AcknowledgedBlock);
    }

    [Fact]
    public void ResendsTheWindowWhenTheRetransmitTimerFires()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-blksize1456-window4"), 1_000_000, clock);

        session.Start();
        session.OnDatagram(Ack(0));

        clock.Advance(TimeSpan.FromSeconds(2));
        TftpStep resent = session.OnRetransmitTimeout();

        Assert.Equal([1, 2, 3, 4], DataOf(resent).Select(d => (int)d.Block));
        Assert.Equal(TftpSessionState.Transferring, session.State);
    }

    [Fact]
    public void GivesUpWhenTheClientDisappearsMidTransfer()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-blksize1456-window4"), 1_000_000, clock);

        session.Start();
        session.OnDatagram(Ack(0));

        TftpStep last = TftpStep.Nothing(TftpSessionState.Transferring);

        for (int attempt = 0; attempt <= TftpLimits.Default.MaxRetries; attempt++)
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            last = session.OnRetransmitTimeout();
        }

        Assert.Equal(TftpSessionState.Failed, session.State);
        Assert.Equal(TftpFailure.ClientGone, session.Failure);
        Assert.Single(last.Actions.OfType<TftpStopRetransmit>());
        Assert.Empty(DataOf(last));
    }

    [Fact]
    public void StopsImmediatelyWhenTheClientSendsAnError()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-blksize1456-window4"), 1_000_000, clock);

        session.Start();
        session.OnDatagram(Ack(0));

        TftpStep step = session.OnDatagram(PacketFixture.Load("Tftp", "error-8-option-negotiation"));

        Assert.Equal(TftpSessionState.Failed, session.State);
        Assert.Equal(TftpFailure.ClientAborted, session.Failure);

        // RFC 1350: an ERROR is not acknowledged.
        Assert.Empty(step.Actions.OfType<TftpSendError>());
    }

    [Fact]
    public void EndsWithAShortBlock()
    {
        TestTimeProvider clock = new(s_start);

        // Two full 512 octet blocks and a 100 octet remainder.
        TftpReadSession session = Session(Request("rrq-wdsmgfw-no-options"), (512 * 2) + 100, clock);

        session.Start();
        session.OnDatagram(Ack(1));
        TftpStep third = session.OnDatagram(Ack(2));

        TftpSendData last = Assert.Single(DataOf(third));
        Assert.Equal(3, last.Block);
        Assert.Equal(100, last.Length);

        session.OnDatagram(Ack(3));
        Assert.Equal(TftpSessionState.Completed, session.State);
    }

    [Fact]
    public void SendsAnEmptyFinalBlockWhenTheFileIsAnExactMultipleOfTheBlockSize()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-no-options"), 512 * 2, clock);

        session.Start();
        session.OnDatagram(Ack(1));
        TftpStep third = session.OnDatagram(Ack(2));

        // RFC 1350: a transfer ends with a block shorter than the block size, so an exact multiple
        // needs one more block carrying nothing.
        TftpSendData last = Assert.Single(DataOf(third));
        Assert.Equal(3, last.Block);
        Assert.Equal(0, last.Length);
    }

    [Fact]
    public void CarriesOnPastTheSixteenBitBlockNumberRollover()
    {
        TestTimeProvider clock = new(s_start);

        // Just over 65536 blocks of 512 octets, which is 32 MB. A boot.wim is many times that, so
        // this is the ordinary case rather than an edge case.
        const long blocks = 65_540;
        TftpReadSession session = Session(Request("rrq-wdsmgfw-no-options"), 512 * blocks, clock);

        TftpStep step = session.Start();
        bool sawWireBlockZero = false;

        while (session.State == TftpSessionState.Transferring)
        {
            List<TftpSendData> data = DataOf(step);

            if (data.Count == 0)
            {
                break;
            }

            sawWireBlockZero |= data.Exists(d => d.Block == 0);
            step = session.OnDatagram(Ack(data[^1].Block));
        }

        Assert.True(sawWireBlockZero, "The transfer never wrapped, so the rollover was not exercised.");
        Assert.Equal(TftpSessionState.Completed, session.State);
        Assert.Equal(blocks + 1, session.AcknowledgedBlock);
    }

    [Fact]
    public void ReadsBlockZeroAsTheWrappedBlockRatherThanAnOptionAckOnceDataIsFlowing()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-no-options"), 512L * 70_000, clock);

        session.Start();

        // Walk to block 65535, then acknowledge wire block 0, which is logical block 65536.
        for (long block = 1; block <= 65_535; block++)
        {
            session.OnDatagram(Ack(unchecked((ushort)block)));
        }

        Assert.Equal(65_535, session.AcknowledgedBlock);

        session.OnDatagram(Ack(0));

        Assert.Equal(65_536, session.AcknowledgedBlock);
        Assert.Equal(TftpSessionState.Transferring, session.State);
    }

    [Fact]
    public void RejectsAPacketThatIsNotAnAcknowledgement()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-no-options"), 10_000, clock);

        session.Start();
        TftpStep step = session.OnDatagram(PacketFixture.Load("Tftp", "rrq-wdsmgfw-no-options"));

        Assert.Equal(TftpSessionState.Failed, session.State);
        Assert.Equal(TftpFailure.IllegalOperation, session.Failure);
        Assert.Single(step.Actions.OfType<TftpSendError>());
    }

    [Fact]
    public void DoesNothingOnceTheTransferHasEnded()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-no-options"), 100, clock);

        session.Start();
        session.OnDatagram(Ack(1));

        Assert.Equal(TftpSessionState.Completed, session.State);
        Assert.Empty(session.OnDatagram(Ack(1)).Actions);
        Assert.Empty(session.OnRetransmitTimeout().Actions);
    }

    [Fact]
    public void BacksOffBetweenRetransmitsAndGivesUpBeforeTheFirmwareDoes()
    {
        TestTimeProvider clock = new(s_start);
        TftpReadSession session = Session(Request("rrq-wdsmgfw-blksize1456-window4"), 1_000_000, clock);

        session.Start();
        session.OnDatagram(Ack(0));

        List<TimeSpan> delays = [];

        for (int attempt = 0; attempt < TftpLimits.Default.MaxRetries; attempt++)
        {
            TftpStep step = session.OnRetransmitTimeout();
            TftpArmRetransmit armed = Assert.Single(step.Actions.OfType<TftpArmRetransmit>());
            delays.Add(armed.Deadline - clock.GetUtcNow());
            clock.Advance(armed.Deadline - clock.GetUtcNow());
        }

        // Doubling from the negotiated timeout, capped, so a lost datagram recovers fast while a
        // dead client is abandoned before EDK2 gives up on DDT somewhere past fifteen seconds.
        Assert.Equal(
            [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4)],
            delays);

        // The first window was armed for one second before any of these, so the whole budget is
        // eleven seconds, comfortably inside the window in which EDK2 is still listening.
        Assert.True(
            delays.Aggregate(TimeSpan.FromSeconds(1), (total, delay) => total + delay) < TimeSpan.FromSeconds(12));
    }
}

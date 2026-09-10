namespace DDT.Protocols.Tftp;

// A read transfer expressed as events in and actions out. It holds no socket, no timer and no file
// handle, so a test can drive a whole transfer with a fake clock and assert every datagram.
public sealed class TftpReadSession
{
    private readonly long _fileLength;
    private readonly TftpLimits _limits;
    private readonly TimeProvider _timeProvider;

    // Block numbers on the wire are 16 bit and roll over on any file past 65535 blocks, which
    // boot.wim comfortably is. The session counts logically in 64 bits and truncates only when a
    // block number is put on the wire.
    private readonly long _finalBlock;
    private long _acknowledged;
    private int _retries;
    private bool _optionAckPending;

    public TftpReadSession(TftpReadRequest request, long fileLength, TftpLimits limits, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegative(fileLength);

        _fileLength = fileLength;
        _limits = limits;
        _timeProvider = timeProvider;
        Negotiated = Negotiate(request.Options, limits, fileLength);

        // RFC 2347: an OACK is sent only when at least one option is accepted, and the client
        // confirms it with an ACK for block zero before any data flows.
        _optionAckPending = Negotiated.AnyAcknowledged;
        State = _optionAckPending ? TftpSessionState.AwaitingOptionAck : TftpSessionState.Transferring;

        // A transfer ends with a block shorter than the block size, so a file that is an exact
        // multiple needs one extra empty block.
        _finalBlock = (_fileLength / Negotiated.BlockSize) + 1;
    }

    public TftpSessionState State { get; private set; }

    public TftpFailure Failure { get; private set; }

    public TftpNegotiation Negotiated { get; }

    public long AcknowledgedBlock => _acknowledged;

    public TftpStep Start() => _optionAckPending
        ? new TftpStep(State, [new TftpSendOptionAck(Negotiated), Retransmit()])
        : SendWindow();

    public TftpStep OnDatagram(ReadOnlySpan<byte> datagram)
    {
        if (State is TftpSessionState.Completed or TftpSessionState.Failed)
        {
            return TftpStep.Nothing(State);
        }

        if (!TftpPacket.TryReadOpcode(datagram, out TftpOpcode opcode))
        {
            return Fail(TftpFailure.IllegalOperation, "Malformed packet");
        }

        if (opcode == TftpOpcode.Error)
        {
            State = TftpSessionState.Failed;
            Failure = TftpFailure.ClientAborted;

            return new TftpStep(State, [new TftpStopRetransmit()]);
        }

        if (!TftpPacket.TryReadAcknowledgement(datagram, out ushort block))
        {
            return Fail(TftpFailure.IllegalOperation, "Expected an acknowledgement");
        }

        // Block zero means the OACK was accepted, but only while one is outstanding. Once data is
        // flowing, block zero is the wrapped block 65536 and is resolved by the logical counter.
        if (_optionAckPending)
        {
            if (block != 0)
            {
                return TftpStep.Nothing(State);
            }

            _optionAckPending = false;
            State = TftpSessionState.Transferring;
            _retries = 0;

            return SendWindow();
        }

        long acknowledged = ResolveBlock(block);

        // RFC 1123 section 4.2.3.1: answering a duplicate acknowledgement restarts the window and
        // produces the Sorcerer's Apprentice packet storm, so a duplicate is ignored outright.
        if (acknowledged <= _acknowledged)
        {
            return TftpStep.Nothing(State);
        }

        _acknowledged = acknowledged;
        _retries = 0;

        if (_acknowledged >= _finalBlock)
        {
            State = TftpSessionState.Completed;

            return new TftpStep(State, [new TftpStopRetransmit()]);
        }

        return SendWindow();
    }

    public TftpStep OnRetransmitTimeout()
    {
        if (State is TftpSessionState.Completed or TftpSessionState.Failed)
        {
            return TftpStep.Nothing(State);
        }

        _retries++;

        if (_retries > _limits.MaxRetries)
        {
            State = TftpSessionState.Failed;
            Failure = TftpFailure.ClientGone;

            return new TftpStep(State, [new TftpStopRetransmit()]);
        }

        return _optionAckPending
            ? new TftpStep(State, [new TftpSendOptionAck(Negotiated), Retransmit()])
            : SendWindow();
    }

    // The wire carries only the low 16 bits, so an acknowledgement names the first logical block at
    // or after the current position whose low bits match.
    private long ResolveBlock(ushort block)
    {
        long distance = (block - _acknowledged) & 0xFFFF;

        return distance == 0 ? _acknowledged : _acknowledged + distance;
    }

    // RFC 7440 section 4: the last received acknowledgement sets the beginning of the next window.
    private TftpStep SendWindow()
    {
        List<TftpAction> actions = new(Negotiated.WindowSize + 1);

        for (int offsetInWindow = 1; offsetInWindow <= Negotiated.WindowSize; offsetInWindow++)
        {
            long logicalBlock = _acknowledged + offsetInWindow;
            long fileOffset = (logicalBlock - 1) * Negotiated.BlockSize;

            if (fileOffset > _fileLength)
            {
                break;
            }

            int length = (int)Math.Min(Negotiated.BlockSize, _fileLength - fileOffset);
            actions.Add(new TftpSendData(unchecked((ushort)logicalBlock), fileOffset, length));

            if (length < Negotiated.BlockSize)
            {
                break;
            }
        }

        actions.Add(Retransmit());

        return new TftpStep(State, actions);
    }

    private TftpStep Fail(TftpFailure failure, string message)
    {
        State = TftpSessionState.Failed;
        Failure = failure;

        return new TftpStep(
            State,
            [new TftpSendError(TftpErrorCode.IllegalOperation, message), new TftpStopRetransmit()]);
    }

    private TftpArmRetransmit Retransmit() => new(_timeProvider.GetUtcNow() + Negotiated.Timeout);

    private static TftpNegotiation Negotiate(TftpRequestedOptions requested, TftpLimits limits, long fileLength)
    {
        // RFC 2348 and RFC 7440 both allow a server only to negotiate downwards.
        int blockSize = requested.BlockSize is int wanted and >= 8
            ? Math.Min(wanted, limits.MaxBlockSize)
            : 512;

        int windowSize = requested.WindowSize is int window and >= 1
            ? Math.Min(window, limits.MaxWindowSize)
            : 1;

        // RFC 2349: the timeout in an OACK must equal the one requested. A clamped value is not a
        // negotiation, it is a malformed OACK, and EDK2 answers that with ERROR 4 and gives up.
        bool timeoutAcceptable = requested.Timeout is >= 1 and <= 255;
        TimeSpan timeout = timeoutAcceptable
            ? TimeSpan.FromSeconds(requested.Timeout!.Value)
            : limits.DefaultTimeout;

        return new TftpNegotiation
        {
            BlockSize = blockSize,
            WindowSize = windowSize,
            Timeout = timeout,
            TransferSize = requested.TransferSize is not null ? fileLength : null,
            AcknowledgeBlockSize = requested.BlockSize is >= 8,
            AcknowledgeWindowSize = requested.WindowSize is >= 1,
            AcknowledgeTimeout = timeoutAcceptable,
            AcknowledgeTransferSize = requested.TransferSize is not null,
        };
    }
}

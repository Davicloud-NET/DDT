namespace DDT.Protocols.Tftp;

// Carries both the values in force and which of them may appear in the OACK. RFC 2347 and the EDK2
// client agree that a server must never acknowledge an option the client did not ask for: EDK2
// answers a non conforming OACK with ERROR 4 and no other diagnostic.
public readonly record struct TftpNegotiation
{
    public required int BlockSize { get; init; }

    public required int WindowSize { get; init; }

    public required TimeSpan Timeout { get; init; }

    public long? TransferSize { get; init; }

    public bool AcknowledgeBlockSize { get; init; }

    public bool AcknowledgeWindowSize { get; init; }

    public bool AcknowledgeTimeout { get; init; }

    public bool AcknowledgeTransferSize { get; init; }

    public bool AnyAcknowledged =>
        AcknowledgeBlockSize || AcknowledgeWindowSize || AcknowledgeTimeout || AcknowledgeTransferSize;
}

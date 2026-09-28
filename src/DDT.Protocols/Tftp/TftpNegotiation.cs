// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Protocols.Tftp;

// The values in force, and which of them the OACK may carry: per RFC 2347 a server never acknowledges an option the
// client did not ask for, and EDK2 answers such an OACK with ERROR 4 and nothing else.
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

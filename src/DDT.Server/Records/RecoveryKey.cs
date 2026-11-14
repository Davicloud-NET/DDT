// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Records;

// A BitLocker recovery password an Enable BitLocker step handed over. RecoveryKeyProtector encrypts it, bound to the
// machine and the protector. A later key for the same machine and volume sets ReplacedUtc.
public sealed class RecoveryKey
{
    public Guid Id { get; set; }

    public Guid MachineId { get; set; }

    // Kept after the run is gone, so no foreign key.
    public Guid RunId { get; set; }

    public Guid ProtectorId { get; set; }

    public required string Volume { get; set; }

    public required string ProtectedPassword { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? ReplacedUtc { get; set; }
}

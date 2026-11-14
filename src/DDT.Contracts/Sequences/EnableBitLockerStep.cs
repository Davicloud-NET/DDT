// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Protects the Windows volume with the TPM and a recovery password, hands the password to the server, and only then
// turns protection on. BackupToActiveDirectory also backs it up to AD on a joined machine. Version 8.
public sealed record EnableBitLockerStep : SequenceStep
{
    public const int MaxWaitMinutes = 1440;

    public const int DefaultWaitMinutes = 120;

    // Set, not init: the generated JSON reader gives a missing init property its type's default, not this one.
    public BitLockerUnavailable IfNoTpm { get; set; } = BitLockerUnavailable.Fail;

    public bool BackupToActiveDirectory { get; set; } = true;

    public bool WaitForEncryption { get; init; }

    public int WaitMinutes { get; set; } = DefaultWaitMinutes;

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.Windows;

    [JsonIgnore]
    public override int MinimumVersion => 8;

    // Each part is checked before it is done again, and the checkpoint keeps what the server confirmed.
    [JsonIgnore]
    public override bool Resumable => true;

    // Windows Server needs its BitLocker feature first, which restarts.
    [JsonIgnore]
    public override int RestartLimit => 2;
}

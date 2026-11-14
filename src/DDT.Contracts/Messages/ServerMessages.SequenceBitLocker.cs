// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // The Pre-provision BitLocker and Enable BitLocker steps.

    public static readonly MessageTemplate SequenceBitLockerBeforePartition = Define(
        "sequence.bitLockerBeforePartition",
        "BitLocker can be pre-provisioned only after a step that partitions the disk.");

    public static readonly MessageTemplate SequenceBitLockerAfterImage = Define(
        "sequence.bitLockerAfterImage",
        "BitLocker can be pre-provisioned only before the image is applied.");

    public static readonly MessageTemplate SequenceOneBitLockerPreprovision = Define(
        "sequence.oneBitLockerPreprovision",
        "A sequence can pre-provision BitLocker only once.");

    public static readonly MessageTemplate SequenceOneBitLocker = Define(
        "sequence.oneBitLocker",
        "A sequence can enable BitLocker only once.");

    public static readonly MessageTemplate SequenceBitLockerWaitMinutes = Define(
        "sequence.bitLockerWaitMinutes",
        "Wait 1 to {max} minutes.");
}

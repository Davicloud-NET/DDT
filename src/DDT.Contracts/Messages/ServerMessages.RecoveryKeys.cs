// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Recovery keys.

    public static readonly MessageTemplate RecoveryKeyUnreadable = Define(
        "recoveryKey.unreadable",
        "The server can't decrypt this key. Its key ring is not the one that saved it.");

    public static readonly MessageTemplate RecoveryKeyCurrent = Define(
        "recoveryKey.current",
        "Only a replaced key can be deleted. This one is the volume's current key.");

    public static readonly MessageTemplate MachineRemoveRecoveryKeys = Define(
        "machine.removeRecoveryKeys",
        "{count, plural, one {The machine has a recovery key, which is deleted with it.} " +
        "other {The machine has # recovery keys, which are deleted with it.}} Confirm to remove it.");
}

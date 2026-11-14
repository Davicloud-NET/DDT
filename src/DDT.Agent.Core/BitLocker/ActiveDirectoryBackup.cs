// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.BitLocker;

// Backs the recovery password up to AD on a joined machine, with the policy values the backup needs set for the call
// only. The checkpoint names the values before they are set, so a cut-off backup's values are removed on the next turn.
// A failed backup fails the step; the key is in DDT already, and protection is still off.
internal sealed class ActiveDirectoryBackup(BitLockerTools tools, AgentLog log)
{
    public async Task RunAsync(EnableBitLockerStep step, string volume, BitLockerCheckpoint checkpoint, StepCheckpoint saved, CancellationToken cancellationToken)
    {
        if (checkpoint.Policy.Count > 0)
        {
            tools.Policy.Remove(checkpoint.Policy);
            checkpoint.Policy = [];
            await saved.SaveAsync(checkpoint.Write()).ConfigureAwait(false);
        }

        if (!step.BackupToActiveDirectory || checkpoint.BackedUp || checkpoint.ProtectorId is not { } protectorId)
        {
            return;
        }

        if (!tools.Domain.IsJoined())
        {
            log.Information("The machine is not joined to a domain, so the recovery password is not backed up to Active Directory.");

            return;
        }

        // Named in the checkpoint before they are set.
        IReadOnlyList<string> set = tools.Policy.Absent(ActiveDirectoryBackupPolicy.Values.Keys);
        checkpoint.Policy = [.. set];
        await saved.SaveAsync(checkpoint.Write()).ConfigureAwait(false);

        foreach (string name in set)
        {
            tools.Policy.Set(name, ActiveDirectoryBackupPolicy.Values[name]);
        }

        try
        {
            log.Information($"Backing up the recovery password of protector {protectorId:B} to Active Directory.");
            await tools.BitLocker.BackupToActiveDirectoryAsync(volume, protectorId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            tools.Policy.Remove(set);
        }

        checkpoint.Policy = [];
        checkpoint.BackedUp = true;
        await saved.SaveAsync(checkpoint.Write()).ConfigureAwait(false);
        log.Information("Active Directory has the recovery password.");
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Rules;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Machines;

// Anyone who reaches the server can register machines, so an operator throws away the ones nobody vouched for.
internal sealed class MachineRemovals(DdtDbContext database, LiveNotifier live, RuleRecount recount, TimeProvider timeProvider)
{
    // Found is false if the machine doesn't exist. Refusal says why it can't be removed. A machine with recovery keys
    // is only removed when recoveryKeys confirms how many go with it.
    public async Task<(bool Found, ServerMessage? Refusal)> RemoveAsync(Guid id, int? recoveryKeys, Actor actor, CancellationToken cancellationToken)
    {
        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return (false, null);
        }

        // A rejected machine stays rejected however often it registers. Removing it is the only way back, and it then
        // registers as a new machine at its next netboot.
        if (!IsStray(machine) && machine.State != MachineState.Rejected)
        {
            return (true, ServerMessages.MachineCannotBeRemoved.With());
        }

        int keys = await database.RecoveryKeys.CountAsync(k => k.MachineId == id, cancellationToken).ConfigureAwait(false);

        if (keys > 0 && recoveryKeys != keys)
        {
            return (true, ServerMessages.MachineRemoveRecoveryKeys.With("count", keys));
        }

        return (true, await RemoveStraysAsync([machine], actor, keys, cancellationToken).ConfigureAwait(false));
    }

    // Removes every waiting machine from the address that nobody vouched for.
    public async Task<ServerMessage?> RemoveWaitingFromAsync(string address, Actor actor, CancellationToken cancellationToken)
    {
        List<Machine> machines = await database.Machines
            .Where(m => m.State == MachineState.Pending
                && m.FirstApprovedUtc == null
                && m.ActiveDeploymentId == null
                && m.FirstSeenAddress == address)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await RemoveStraysAsync(machines, actor, 0, cancellationToken).ConfigureAwait(false);
    }

    private static string KeysDeleted(int keys) => keys switch
    {
        0 => string.Empty,
        1 => " Its recovery key was deleted with it.",
        _ => $" Its {keys} recovery keys were deleted with it.",
    };

    // A waiting machine with an assigned run waits on purpose, for a sign-in or a zero touch netboot.
    private static bool IsStray(Machine machine) =>
        machine.State == MachineState.Pending && machine.FirstApprovedUtc is null && machine.ActiveDeploymentId is null;

    // A stray was never approved, so it never ran and has no keys. Keys counts those of a single rejected machine.
    private async Task<ServerMessage?> RemoveStraysAsync(List<Machine> machines, Actor actor, int keys, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (Machine machine in machines)
        {
            database.AuditEvents.Add(AuditEvents.Create(
                AuditActions.MachineRemoved,
                machine.Id.ToString("D"),
                actor,
                now,
                (machine.State == MachineState.Rejected
                    ? $"Rejected, first seen {machine.FirstSeenUtc:u} from {machine.FirstSeenAddress}."
                    : $"Waiting since {machine.FirstSeenUtc:u} from {machine.FirstSeenAddress}.")
                + KeysDeleted(keys)));
        }

        database.Machines.RemoveRange(machines);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServerMessages.MachineChangedWhileRemoving.With();
        }

        if (machines.Count > 0)
        {
            live.MachinesRemoved(machines.Select(m => m.Id));
            recount.MachinesChanged();
        }

        return null;
    }
}

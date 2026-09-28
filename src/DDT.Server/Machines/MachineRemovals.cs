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
    // Found is false for a machine that is gone; Refusal says why one cannot be removed.
    public async Task<(bool Found, ServerMessage? Refusal)> RemoveAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return (false, null);
        }

        // A rejected machine stays rejected however often it registers, so removing it is the only way back: it then
        // registers as a new machine at its next netboot.
        if (!IsStray(machine) && machine.State != MachineState.Rejected)
        {
            return (true, ServerMessages.MachineCannotBeRemoved.With());
        }

        return (true, await RemoveStraysAsync([machine], actor, cancellationToken).ConfigureAwait(false));
    }

    // Every machine waiting from the address that nobody vouched for.
    public async Task<ServerMessage?> RemoveWaitingFromAsync(string address, Actor actor, CancellationToken cancellationToken)
    {
        List<Machine> machines = await database.Machines
            .Where(m => m.State == MachineState.Pending
                && m.FirstApprovedUtc == null
                && m.ActiveDeploymentId == null
                && m.FirstSeenAddress == address)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await RemoveStraysAsync(machines, actor, cancellationToken).ConfigureAwait(false);
    }

    // A waiting machine with an assigned run waits on purpose, for a sign-in or a zero touch netboot.
    private static bool IsStray(Machine machine) =>
        machine.State == MachineState.Pending && machine.FirstApprovedUtc is null && machine.ActiveDeploymentId is null;

    private async Task<ServerMessage?> RemoveStraysAsync(List<Machine> machines, Actor actor, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        foreach (Machine machine in machines)
        {
            database.AuditEvents.Add(AuditEvents.Create(
                AuditActions.MachineRemoved,
                machine.Id.ToString("D"),
                actor,
                now,
                machine.State == MachineState.Rejected
                    ? $"Rejected, first seen {machine.FirstSeenUtc:u} from {machine.FirstSeenAddress}."
                    : $"Waiting since {machine.FirstSeenUtc:u} from {machine.FirstSeenAddress}."));
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

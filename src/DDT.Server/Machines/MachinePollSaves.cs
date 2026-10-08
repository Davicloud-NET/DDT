// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Live;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Machines;

// Saves what a poll changed on the machine and its run, and pushes it to the web.
internal sealed class MachinePollSaves(DdtDbContext database, RunQueries queries, LiveNotifier live)
{
    // False if the save lost to an approval, a rejection, an assignment or a registration. What the poll wanted to
    // record can wait for the next one, but its answer has to reflect what's stored now, so the machine is read again.
    public async Task<bool> SaveAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            live.MachineChanged(machine, await queries.ShownAsync(machine, cancellationToken).ConfigureAwait(false));

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await database.Entry(machine).ReloadAsync(cancellationToken).ConfigureAwait(false);

            return false;
        }
    }
}

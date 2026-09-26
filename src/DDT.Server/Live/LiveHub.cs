// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;

namespace DDT.Server.Live;

// Server to client: clients receive small change events and patch or refetch their queries. A page that shows one
// machine watches it, to receive also what only it needs, such as new log lines and step changes. Groups do not
// survive a reconnect, so a client watches again after one.
public sealed class LiveHub(SignInManager<DdtUser> signInManager, UserManager<DdtUser> userManager, LiveConnections connections) : Hub
{
    // A connection is one browser tab, which shows a machine or a few.
    public const int MaxWatchedMachines = 16;

    private const string WatchedKey = "ddt.watched";

    // The cookie carries the roles and the security stamp of its last check, up to a minute old, so the account is read
    // as it is now: one disabled, deleted or signed out everywhere since then gets no connection, and administrators,
    // who also receive what only they may read, are told apart by the roles they hold now. A role changed while the
    // connection is open takes effect when it connects again, which the Users API forces by closing it.
    public override async Task OnConnectedAsync()
    {
        DdtUser? user = Context.User is { } principal
            ? await signInManager.ValidateSecurityStampAsync(principal).ConfigureAwait(false)
            : null;

        if (user is null || user.IsDisabled)
        {
            Context.Abort();

            return;
        }

        connections.Opened(user.Id, Context);

        if (await userManager.IsInRoleAsync(user, DdtRoleNames.Administrator).ConfigureAwait(false))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.Administrators, Context.ConnectionAborted).ConfigureAwait(false);
        }

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Closed(Context.ConnectionId);

        return base.OnDisconnectedAsync(exception);
    }

    public async Task WatchMachine(Guid machineId)
    {
        HashSet<Guid> watched = Watched();

        if (!watched.Contains(machineId) && watched.Count >= MaxWatchedMachines)
        {
            throw new HubException($"A connection watches at most {MaxWatchedMachines} machines. Stop watching one first.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.Machine(machineId), Context.ConnectionAborted).ConfigureAwait(false);
        watched.Add(machineId);
    }

    public async Task UnwatchMachine(Guid machineId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveGroups.Machine(machineId), Context.ConnectionAborted).ConfigureAwait(false);
        Watched().Remove(machineId);
    }

    // The hub itself lives for one call, so what a connection watches is kept with the connection.
    private HashSet<Guid> Watched()
    {
        if (Context.Items.TryGetValue(WatchedKey, out object? value) && value is HashSet<Guid> watched)
        {
            return watched;
        }

        HashSet<Guid> created = [];
        Context.Items[WatchedKey] = created;

        return created;
    }
}

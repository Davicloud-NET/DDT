// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;

namespace DDT.Server.Live;

// Pushes what changed for clients to patch in (see LiveEvents). A page watches the machine it shows, again after a
// reconnect, since groups do not survive one; an API token receives what its role may read.
public sealed class LiveHub(SignInManager<DdtUser> signInManager, UserManager<DdtUser> userManager, LiveConnections connections) : Hub
{
    // A connection is one browser tab, which shows a machine or a few.
    public const int MaxWatchedMachines = 16;

    private const string WatchedKey = "ddt.watched";

    // The cookie's roles and security stamp can be a minute old, so the account is read as it is now: a disabled, deleted
    // or signed-out one gets no connection. The token handler settled a token's role already. A role changed while the
    // connection is open counts when it connects again, which the Users API forces by closing it.
    public override async Task OnConnectedAsync()
    {
        Guid? userId;
        bool administrator;
        bool @operator;

        if (Context.User is { } token && Principals.ApiTokenId(token) is not null)
        {
            userId = Principals.UserId(token);
            administrator = token.IsInRole(DdtRoleNames.Administrator);
            @operator = token.IsInRole(DdtRoleNames.Operator);
        }
        else
        {
            DdtUser? user = Context.User is { } principal
                ? await signInManager.ValidateSecurityStampAsync(principal).ConfigureAwait(false)
                : null;

            if (user is null || user.IsDisabled)
            {
                Context.Abort();

                return;
            }

            userId = user.Id;
            administrator = await userManager.IsInRoleAsync(user, DdtRoleNames.Administrator).ConfigureAwait(false);
            @operator = await userManager.IsInRoleAsync(user, DdtRoleNames.Operator).ConfigureAwait(false);
        }

        if (userId is not { } id)
        {
            Context.Abort();

            return;
        }

        connections.Opened(id, Context);

        if (administrator)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.Administrators, Context.ConnectionAborted).ConfigureAwait(false);
        }
        else if (@operator)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.Operators, Context.ConnectionAborted).ConfigureAwait(false);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.User(id), Context.ConnectionAborted).ConfigureAwait(false);

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

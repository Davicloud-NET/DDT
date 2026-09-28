// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Endpoints;
using DDT.Server.Live;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Machines;

// What an agent learns when it polls: its tokens, its run and whether someone at the machine may pick a sequence.
internal sealed class MachinePolls(
    DdtDbContext database,
    MachineTokenService tokens,
    RunQueries queries,
    SequenceChoices choices,
    LiveNotifier live,
    DdtSettings settings)
{
    // Null if the machine started over after the token was checked.
    public async Task<AgentNextResult?> NextAsync(Machine machine, ClaimsPrincipal user, DateTimeOffset now, string? address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (LastSeen.Record(machine, now, address) && !await SaveSeenAsync(machine, user, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        // A waiting machine learns nothing about what it will get, because anyone can register as it. An agent from
        // before task sequences never gets an image deployment, because this server doesn't create them.
        bool authorized = machine.State is MachineState.Approved or MachineState.Deploying or MachineState.Failed;
        Deployment? active = authorized ? await queries.ActiveAsync(machine, cancellationToken).ConfigureAwait(false) : null;
        AgentRun? run = active is null ? null : await queries.HandOverAsync(machine, active, cancellationToken).ConfigureAwait(false);
        bool canPick = await choices.CanPickAsync(machine, cancellationToken).ConfigureAwait(false);

        return new AgentNextResult(
            machine.State,
            tokens.IssueCurrent(machine),
            tokens.Issue(machine, MachineTokenPurpose.Resume),
            MachineRegistrar.PollAfterSeconds,
            machine.SignedInUserName,
            Deployment: null,
            CanPickImage: false,
            DomainConfigured: authorized && DeploymentPolicy.IsDomainConfigured(settings.Current),
            AssignedName: authorized ? machine.AssignedName : null,
            Run: run,
            CanPickSequence: canPick,
            SuggestedSequenceId: canPick ? await choices.SuggestedAsync(machine, cancellationToken).ConfigureAwait(false) : null);
    }

    // Returns false if the save lost to an approval, a rejection, an assignment or a registration, and the machine
    // started over. Last seen can wait for the next poll, but the answer has to reflect what's stored now.
    private async Task<bool> SaveSeenAsync(Machine machine, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            live.MachineChanged(machine, await queries.ShownAsync(machine, cancellationToken).ConfigureAwait(false));

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await database.Entry(machine).ReloadAsync(cancellationToken).ConfigureAwait(false);

            return Principals.HoldsCurrentGeneration(user, machine);
        }
    }
}

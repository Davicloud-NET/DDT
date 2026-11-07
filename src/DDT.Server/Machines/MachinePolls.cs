// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Server.Deployments;
using DDT.Server.Endpoints;
using DDT.Server.Settings;

namespace DDT.Server.Machines;

// What an agent learns when it polls: its tokens, its run and whether someone at the machine may pick a sequence.
internal sealed class MachinePolls(
    MachineTokenService tokens,
    RunQueries queries,
    SequenceChoices choices,
    RestartOrders restarts,
    MachinePollSaves saves,
    DdtSettings settings)
{
    // Null if the machine started over after the token was checked.
    public async Task<AgentNextResult?> NextAsync(Machine machine, ClaimsPrincipal user, DateTimeOffset now, string? address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        // Both are tried. A machine that still polls long after it was asked to restart did not get there.
        bool changed = LastSeen.Record(machine, now, address) | PendingSwitch.Expire(machine, now);

        if (changed && !await SavedAsync(machine, user, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        // A waiting machine learns nothing about what it will get, because anyone can register as it. An agent from
        // before task sequences never gets an image deployment, because this server doesn't create them.
        bool authorized = machine.State is MachineState.Approved or MachineState.Deploying or MachineState.Failed;
        Deployment? active = authorized ? await queries.ActiveAsync(machine, cancellationToken).ConfigureAwait(false) : null;
        AgentRun? run = active is null ? null : await queries.HandOverAsync(machine, active, cancellationToken).ConfigureAwait(false);
        RestartOrder restart = active is null || run is not null
            ? RestartOrder.None
            : await restarts.ForAsync(machine, active, address, now, cancellationToken).ConfigureAwait(false);

        // A restart that could not be recorded is not ordered. The next poll orders it.
        if (restart.Changed && !await saves.SaveAsync(machine, cancellationToken).ConfigureAwait(false))
        {
            if (!Principals.HoldsCurrentGeneration(user, machine))
            {
                return null;
            }

            restart = RestartOrder.None;
        }

        // A run that waits where it starts waits for a newer agent, and the person at the machine is told so.
        string? waits = restart.WaitReason
            ?? (active is null || run is not null ? null : await queries.TooOldAsync(machine, active, cancellationToken).ConfigureAwait(false));
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
            SuggestedSequenceId: canPick ? await choices.SuggestedAsync(machine, cancellationToken).ConfigureAwait(false) : null,
            RestartInto: restart.Into,
            WaitReason: waits);
    }

    // False if the save lost to a change that made the machine start over. Last seen can wait for the next poll.
    private async Task<bool> SavedAsync(Machine machine, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await saves.SaveAsync(machine, cancellationToken).ConfigureAwait(false) || Principals.HoldsCurrentGeneration(user, machine);
}

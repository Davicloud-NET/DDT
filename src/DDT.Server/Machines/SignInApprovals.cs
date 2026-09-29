// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Agents;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Endpoints;
using DDT.Server.Live;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Machines;

// Records who signed in at a waiting machine. It also approves it, unless RequireWebApproval needs a web approval too.
internal sealed class SignInApprovals(
    DdtDbContext database,
    RunQueries queries,
    LiveNotifier live,
    DdtSettings settings,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory)
{
    // User is the machine's token. It's checked again if the save loses to a change of the machine.
    public async Task<MachineSignInOutcome> SignedInAsync(Machine machine, Actor signer, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(signer);

        DateTimeOffset now = timeProvider.GetUtcNow();
        Deployment? active = await queries.ActiveAsync(machine, cancellationToken).ConfigureAwait(false);

        machine.SignedInByUserId = signer.UserId;
        machine.SignedInUserName = signer.Name;
        machine.SignedInUtc = now;
        database.AuditEvents.Add(AuditEvents.Create(AuditActions.MachineSignedIn, machine.Id.ToString("D"), signer, now, "Signed in at the machine."));
        Approve(machine, active, signer, now);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception) when (exception.Entries.Any(entry => entry.Entity is Machine))
        {
            await database.Entry(machine).ReloadAsync(cancellationToken).ConfigureAwait(false);

            return Principals.HoldsCurrentGeneration(user, machine)
                ? MachineSignInOutcome.Answered(AgentSignInStatus.AlreadyDecided)
                : MachineSignInOutcome.StartedOver;
        }

        AuthLog.SignedInAtMachine(loggerFactory.CreateLogger(typeof(AgentEndpoints)), signer.Name ?? "", machine.Id);
        live.MachineChanged(machine, active ?? await queries.ShownAsync(machine, cancellationToken).ConfigureAwait(false));

        return MachineSignInOutcome.Answered(AgentSignInStatus.Succeeded);
    }

    // Under RequireWebApproval the sign-in approves only a machine an operator assigned a sequence on the web.
    private void Approve(Machine machine, Deployment? active, Actor signer, DateTimeOffset now)
    {
        bool requireWebApproval = settings.Current.Machines.RequireWebApproval;

        if (requireWebApproval && !DeploymentPolicy.CountsAsWebApproval(active))
        {
            return;
        }

        machine.Approve(signer.UserId, now);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.MachineApproved,
            machine.Id.ToString("D"),
            signer,
            now,
            requireWebApproval && active is not null
                ? $"Was Pending. Signed in at the machine, which {active.RequestedByName} had assigned {active.Title} on the web."
                : "Was Pending. Signed in at the machine."));
    }
}

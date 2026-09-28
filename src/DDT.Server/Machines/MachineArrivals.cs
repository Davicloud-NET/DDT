// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Rules;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Machines;

// Which machine a registration comes from, and what that does to it before its facts are copied: a new machine, one
// that continues its run, or one that starts over. Nothing here saves.
public sealed class MachineArrivals(
    DdtDbContext database,
    MachineTokenService tokens,
    RunQueries queries,
    RunTermination termination,
    DdtSettings settings,
    ILogger<MachineRegistrar> logger)
{
    public async Task<Arrival> RecordAsync(
        NormalisedRegistration registration,
        IPAddress? remoteAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        string? address = remoteAddress?.ToString();
        Machine? machine = await FindAsync(registration, cancellationToken).ConfigureAwait(false);
        string? tested = machine is null ? null : RuleRecount.Tested(machine);
        Deployment? active = machine is null ? null : await queries.ActiveAsync(machine, cancellationToken).ConfigureAwait(false);
        DeploymentState? before = active?.State;
        Continuation continuation = machine is null ? Continuation.None : Continues(registration, machine, active);

        // The service in Windows only ever continues a run. Starting over would make the machine Pending, and an
        // approval would then hand Windows PE steps to a running Windows. It changes nothing, and removes itself.
        if (registration.Environment == AgentEnvironment.Windows && !continuation.Resumes && continuation.Continued is null)
        {
            RegistrationLog.NothingToContinue(logger, machine?.Id, address ?? "unknown");

            return Arrival.Refused(RegistrationRefusal.NothingToContinue);
        }

        if (machine is null)
        {
            machine = await AddNewAsync(registration, address, now, cancellationToken).ConfigureAwait(false);

            if (machine is null)
            {
                return Arrival.Refused(RegistrationRefusal.TooManyWaiting);
            }
        }
        else
        {
            await RecordReturnAsync(new Visit(machine, active, continuation, registration, remoteAddress, now), cancellationToken).ConfigureAwait(false);
        }

        RecordRefusedRunToken(new Visit(machine, active, continuation, registration, remoteAddress, now));

        return new Arrival(machine, active, before, continuation.Continued, tested, RegistrationRefusal.None);
    }

    // Null when too many machines nobody approved are waiting. Only those count, so a fleet that was deployed and booted
    // again never blocks a new machine.
    private async Task<Machine?> AddNewAsync(
        NormalisedRegistration registration,
        string? address,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IQueryable<Machine> waiting = database.Machines.Where(m => m.State == MachineState.Pending && m.FirstApprovedUtc == null);
        MachinePolicy policy = settings.Current.Machines;

        if (await waiting.CountAsync(m => m.FirstSeenAddress == address, cancellationToken).ConfigureAwait(false) >= policy.MaxWaitingPerAddress
            || await waiting.CountAsync(cancellationToken).ConfigureAwait(false) >= policy.MaxWaiting)
        {
            RegistrationLog.TooManyWaiting(logger, address ?? "unknown");

            return null;
        }

        Machine machine = new()
        {
            Id = Guid.CreateVersion7(now),
            SmbiosUuid = registration.SmbiosUuid,
            PrimaryMac = registration.PrimaryMac,
            FirstSeenUtc = now,
            FirstSeenAddress = address,
        };

        database.Machines.Add(machine);
        database.AuditEvents.Add(AuditEvents.Create(AuditActions.MachineRegistered, machine.Id.ToString("D"), Actor.OfMachine(machine.Id, address), now, null));

        return machine;
    }

    // A machine the server knows: still rejected, continuing its run, or starting over.
    private async Task RecordReturnAsync(Visit visit, CancellationToken cancellationToken)
    {
        (Machine machine, Continuation continuation) = (visit.Machine, visit.Continuation);
        string? address = visit.RemoteAddress?.ToString();
        Actor actor = Actor.OfMachine(machine.Id, address);

        if (machine.State == MachineState.Rejected)
        {
            database.AuditEvents.Add(AuditEvents.Create(AuditActions.MachineReregistered, machine.Id.ToString("D"), actor, visit.Now, "Still rejected."));
        }
        else if (continuation.Continued is { } continued)
        {
            AgentEnvironment environment = visit.Registration.Environment;
            database.AuditEvents.Add(AuditEvents.Create(
                AuditActions.DeploymentResumed,
                continued.Id.ToString("D"),
                actor,
                visit.Now,
                $"Continued {continued.Title} ({continued.Id:D}) from {environment} with its {(continuation.ByRunToken ? "run" : "resume")} token."));
            RegistrationLog.Continued(logger, machine.Id, continued.Id, address ?? "unknown", environment);
        }
        else if (!continuation.Resumes)
        {
            await StartOverAsync(visit, cancellationToken).ConfigureAwait(false);
        }
    }

    // Anyone who reaches the server can present a machine's UUID and MAC, so without its resume token or its run's token
    // a registration starts over: the approval is dropped, and the generation bump kills every token issued so far.
    // Zero touch keeps the approval, and the earlier approver, for a web assignment netbooting from a listed network.
    private async Task StartOverAsync(Visit visit, CancellationToken cancellationToken)
    {
        (Machine machine, Deployment? active) = (visit.Machine, visit.Active);
        string? address = visit.RemoteAddress?.ToString();
        Deployment? kept = DeploymentPolicy.KeepsApprovalOnNetboot(settings.Current, active, visit.RemoteAddress) ? active : null;

        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.MachineReregistered,
            machine.Id.ToString("D"),
            Actor.OfMachine(machine.Id, address),
            visit.Now,
            kept is not null
                ? $"Kept approved for {kept.Title} assigned by {kept.RequestedByName}: netbooted from {address} in a zero touch network."
                : $"Was {machine.State}."));

        await termination.EndForRestartAsync(machine, active, address, visit.Registration.RunToken is not null, cancellationToken).ConfigureAwait(false);

        machine.TokenGeneration++;
        machine.SignedInByUserId = null;
        machine.SignedInUserName = null;
        machine.SignedInUtc = null;

        if (kept is not null)
        {
            machine.State = MachineState.Approved;
            machine.ApprovedByUserId ??= kept.RequestedByUserId;
            machine.ApprovedUtc ??= visit.Now;
            machine.FirstApprovedUtc ??= visit.Now;
        }
        else
        {
            machine.State = MachineState.Pending;
            machine.ApprovedByUserId = null;
            machine.ApprovedUtc = null;
        }
    }

    private void RecordRefusedRunToken(Visit visit)
    {
        Machine machine = visit.Machine;

        if (machine.State != MachineState.Rejected && visit.Registration.RunToken is not null && !visit.Continuation.ByRunToken)
        {
            database.AuditEvents.Add(AuditEvents.Create(
                AuditActions.DeploymentRunTokenRefused,
                machine.Id.ToString("D"),
                Actor.OfMachine(machine.Id, visit.RemoteAddress?.ToString()),
                visit.Now,
                "Presented a run token the server no longer accepts: its run is over, or the machine started over since."));
        }
    }

    // A resume keeps the machine's run as well, so it is answered as one: an agent told of no run takes it for over.
    private Continuation Continues(NormalisedRegistration registration, Machine machine, Deployment? active)
    {
        bool resumes = Resumes(registration, machine);
        bool byRunToken = ContinuesRun(registration, machine, active);

        return new Continuation(resumes, byRunToken, byRunToken || (resumes && active is { State: DeploymentState.Running }) ? active : null);
    }

    private bool Resumes(NormalisedRegistration registration, Machine machine) =>
        registration.ResumeToken is { } presented
        && tokens.Validate(presented, MachineTokenPurpose.Resume) is { } payload
        && payload.MachineId == machine.Id
        && payload.TokenGeneration == machine.TokenGeneration;

    // An agent that restarted during its run, in Windows PE or as the service in Windows, continues it with the run token
    // it kept on disk, in the generation it was issued in. It keeps the generation, so its other tokens stay valid.
    private bool ContinuesRun(NormalisedRegistration registration, Machine machine, Deployment? active) =>
        tokens.ValidateRunToken(registration.RunToken) is { } payload
        && payload.MachineId == machine.Id
        && payload.TokenGeneration == machine.TokenGeneration
        && active is { State: DeploymentState.Running }
        && active.Id == payload.RunId;

    // The same machine has the exact UUID string, so one without a UUID never lands on one with, and at least one of its
    // network adapters: cloned virtual machines share a UUID, and cheap firmware reports SMBIOS's all-zeros "no UUID".
    private async Task<Machine?> FindAsync(NormalisedRegistration registration, CancellationToken cancellationToken)
    {
        List<Machine> candidates = await database.Machines
            .Where(m => m.SmbiosUuid == registration.SmbiosUuid)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidates
            .Where(m => m.MacAddresses.Split(',').Intersect(registration.MacAddresses).Any())
            .OrderBy(m => m.FirstSeenUtc)
            .FirstOrDefault();
    }

    // ByRunToken says the run token continued the run; Continued is the run the agent goes on with.
    private sealed record Continuation(bool Resumes, bool ByRunToken, Deployment? Continued)
    {
        public static Continuation None { get; } = new(false, false, null);
    }

    private sealed record Visit(
        Machine Machine,
        Deployment? Active,
        Continuation Continuation,
        NormalisedRegistration Registration,
        IPAddress? RemoteAddress,
        DateTimeOffset Now);
}

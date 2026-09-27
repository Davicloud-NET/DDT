// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Live;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DeploymentStep = DDT.Server.Deployments.DeploymentStep;

namespace DDT.Server.Machines;

public sealed partial class MachineRegistrar(
    DdtDbContext database,
    MachineTokenService tokens,
    DeploymentService deployments,
    LiveNotifier live,
    DdtSettings settings,
    TimeProvider timeProvider,
    ILogger<MachineRegistrar> logger)
{
    public const int PollAfterSeconds = 10;

    private const int MaxAttempts = 3;

    private string? ConsoleLanguage => settings.Current.Deployment.ConsoleLanguage;

    public async Task<MachineRegistration> RegisterAsync(
        NormalisedRegistration registration,
        IPAddress? remoteAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await TryRegisterAsync(registration, remoteAddress, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // An approval, rejection, assignment, report or other registration changed the machine in between.
                // Decide again from what is stored now rather than overwrite it.
                database.ChangeTracker.Clear();
            }
        }
    }

    // The issued tokens are the machine's own tokens for its current state, as NextAsync hands out. Done takes no
    // token at all: the agent reboots after reporting it, and anything still holding a token is not that agent.
    public string CurrentToken(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return tokens.Issue(
            machine,
            machine.State is MachineState.Approved or MachineState.Deploying or MachineState.Failed
                ? MachineTokenPurpose.Session
                : MachineTokenPurpose.Poll);
    }

    private async Task<MachineRegistration> TryRegisterAsync(
        NormalisedRegistration registration,
        IPAddress? remoteAddress,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string? address = remoteAddress?.ToString();
        Machine? machine = await FindAsync(registration, cancellationToken).ConfigureAwait(false);
        Deployment? active = machine is null ? null : await deployments.ActiveAsync(machine, cancellationToken).ConfigureAwait(false);
        DeploymentState? before = active?.State;
        bool resumes = machine is not null && Resumes(registration, machine);
        bool byRunToken = machine is not null && ContinuesRun(registration, machine, active);

        // A resume keeps the machine's run as well, so it is answered as one: an agent told of no run takes it for over.
        Deployment? continued = byRunToken || (resumes && active is { State: DeploymentState.Running }) ? active : null;

        // The service in Windows only ever continues a run. Starting over would make the machine Pending, and an
        // approval would then hand Windows PE steps to a running Windows. It changes nothing, and removes itself.
        if (registration.Environment == AgentEnvironment.Windows && !resumes && continued is null)
        {
            LogNothingToContinue(machine?.Id, address ?? "unknown");

            return MachineRegistration.Refused(RegistrationRefusal.NothingToContinue);
        }

        if (machine is null)
        {
            if (await TooManyWaitingAsync(address, cancellationToken).ConfigureAwait(false))
            {
                LogTooManyWaiting(address ?? "unknown");

                return MachineRegistration.Refused(RegistrationRefusal.TooManyWaiting);
            }

            machine = new Machine
            {
                Id = Guid.CreateVersion7(now),
                SmbiosUuid = registration.SmbiosUuid,
                PrimaryMac = registration.PrimaryMac,
                FirstSeenUtc = now,
                FirstSeenAddress = address,
            };

            database.Machines.Add(machine);
            database.AuditEvents.Add(Audit(now, AuditActions.MachineRegistered, machine, address));
        }
        else if (machine.State == MachineState.Rejected)
        {
            database.AuditEvents.Add(Audit(now, AuditActions.MachineReregistered, machine, address, "Still rejected."));
        }
        else if (continued is not null)
        {
            database.AuditEvents.Add(Audit(
                now,
                AuditActions.DeploymentResumed,
                machine,
                address,
                $"Continued {continued.Title} ({continued.Id:D}) from {registration.Environment} with its {(byRunToken ? "run" : "resume")} token.",
                continued.Id));
            LogContinued(machine.Id, continued.Id, address ?? "unknown", registration.Environment);
        }
        else if (!resumes)
        {
            await StartOverAsync(machine, active, remoteAddress, address, now, registration.RunToken is not null, cancellationToken).ConfigureAwait(false);
        }

        if (machine.State != MachineState.Rejected && registration.RunToken is not null && !byRunToken)
        {
            database.AuditEvents.Add(Audit(
                now,
                AuditActions.DeploymentRunTokenRefused,
                machine,
                address,
                "Presented a run token the server no longer accepts: its run is over, or the machine started over since."));
        }

        machine.PrimaryMac = registration.PrimaryMac;
        machine.MacAddresses = string.Join(',', registration.MacAddresses);
        machine.Manufacturer = registration.Manufacturer;
        machine.Model = registration.Model;
        machine.SerialNumber = registration.SerialNumber;
        machine.AgentVersion = registration.AgentVersion;
        machine.SequenceVersion = registration.SequenceVersion;
        machine.AgentEnvironment = registration.Environment;
        machine.SecureBootEnabled = registration.SecureBootEnabled;
        machine.TrustedUefiCas = registration.TrustedUefiCas;
        machine.ChassisType = registration.ChassisType;
        machine.LastSeenUtc = now;
        machine.LastSeenAddress = address;

        // An older agent reports no disks. What a newer one reported before stays, rather than turn unknown.
        if (registration.EligibleDiskCount is not null)
        {
            machine.Disks = registration.Disks;
            machine.EligibleDiskCount = registration.EligibleDiskCount;
        }

        IReadOnlyList<DeploymentStep> changedSteps = RunReports.ChangedSteps(database);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (active is not null)
        {
            DeploymentLog.Changed(logger, active, before);
        }

        live.RunStepsChanged(machine.Id, changedSteps);
        live.MachineChanged(machine, await deployments.ShownAsync(machine, cancellationToken).ConfigureAwait(false));

        return new MachineRegistration(
            machine.State == MachineState.Rejected
                ? new AgentRegistrationResult(machine.Id, machine.State, null, null, PollAfterSeconds, null, ConsoleLanguage: ConsoleLanguage)
                : new AgentRegistrationResult(
                    machine.Id,
                    machine.State,
                    CurrentToken(machine),
                    tokens.Issue(machine, MachineTokenPurpose.Resume),
                    PollAfterSeconds,
                    machine.SignedInUserName,
                    continued?.Id,
                    continued is null ? null : tokens.IssueRunToken(machine, continued.Id),
                    ConsoleLanguage),
            RegistrationRefusal.None);
    }

    // Anyone who reaches the server can present a machine's UUID and MAC. Unless the registration proves it comes
    // from the agent already holding this machine, with its resume token or its run's token, it starts over: any
    // approval is dropped and every token issued so far dies with the generation bump, so two agents can never share
    // one machine's tokens. The one exception is zero touch: an operator assigned a sequence on the web, and the
    // machine netboots from a network listed for that. What the agent that is gone had started or chosen ends, see
    // DeploymentService.EndForRestartAsync.
    private async Task StartOverAsync(
        Machine machine,
        Deployment? active,
        IPAddress? remoteAddress,
        string? address,
        DateTimeOffset now,
        bool presentedRunToken,
        CancellationToken cancellationToken)
    {
        bool zeroTouch = deployments.KeepsApprovalOnNetboot(active, remoteAddress);

        database.AuditEvents.Add(Audit(
            now,
            AuditActions.MachineReregistered,
            machine,
            address,
            zeroTouch
                ? $"Kept approved for {active!.Title} assigned by {active.RequestedByName}: netbooted from {address} in a zero touch network."
                : $"Was {machine.State}."));

        await deployments.EndForRestartAsync(machine, active, address, presentedRunToken, cancellationToken).ConfigureAwait(false);

        machine.TokenGeneration++;
        machine.SignedInByUserId = null;
        machine.SignedInUserName = null;
        machine.SignedInUtc = null;

        if (zeroTouch)
        {
            machine.State = MachineState.Approved;
            machine.ApprovedByUserId ??= active!.RequestedByUserId;
            machine.ApprovedUtc ??= now;
            machine.FirstApprovedUtc ??= now;
        }
        else
        {
            machine.State = MachineState.Pending;
            machine.ApprovedByUserId = null;
            machine.ApprovedUtc = null;
        }
    }

    // Only machines nobody has approved count, so a fleet that was deployed and booted again never blocks a
    // new machine.
    private async Task<bool> TooManyWaitingAsync(string? address, CancellationToken cancellationToken)
    {
        IQueryable<Machine> waiting = database.Machines
            .Where(m => m.State == MachineState.Pending && m.FirstApprovedUtc == null);

        MachinePolicy policy = settings.Current.Machines;

        return await waiting.CountAsync(m => m.FirstSeenAddress == address, cancellationToken).ConfigureAwait(false) >= policy.MaxWaitingPerAddress
            || await waiting.CountAsync(cancellationToken).ConfigureAwait(false) >= policy.MaxWaiting;
    }

    private bool Resumes(NormalisedRegistration registration, Machine machine) =>
        registration.ResumeToken is { } presented
        && tokens.Validate(presented, MachineTokenPurpose.Resume) is { } payload
        && payload.MachineId == machine.Id
        && payload.TokenGeneration == machine.TokenGeneration;

    // An agent that restarted during its run, in Windows PE or as the service in Windows, continues it with the run
    // token it kept on disk: its run is the machine's active one and still running, in the generation it was issued in.
    // It keeps the generation, so the tokens the agent held before the restart stay valid.
    private bool ContinuesRun(NormalisedRegistration registration, Machine machine, Deployment? active) =>
        tokens.ValidateRunToken(registration.RunToken) is { } payload
        && payload.MachineId == machine.Id
        && payload.TokenGeneration == machine.TokenGeneration
        && active is { State: DeploymentState.Running }
        && active.Id == payload.RunId;

    // A machine is the same one when the UUID matches exactly and at least one of its network adapters is
    // still present. A UUID alone is not enough: cloned virtual machines and some boards share one, and
    // SMBIOS reserves all zeros and all ones for "no UUID", which cheap firmware reports. Matching on the
    // exact UUID string also keeps a machine without a UUID from ever landing on one that has one.
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

    private static AuditEvent Audit(
        DateTimeOffset now,
        string action,
        Machine machine,
        string? address,
        string? detail = null,
        Guid? subjectId = null) => new()
        {
            OccurredUtc = now,
            Action = action,
            ActorMachineId = machine.Id,
            SubjectId = (subjectId ?? machine.Id).ToString("D"),
            SourceAddress = address,
            Detail = detail,
        };

    [LoggerMessage(EventId = 410, Level = LogLevel.Warning, Message = "Refused a new machine from {Address}: too many machines nobody approved are waiting")]
    private partial void LogTooManyWaiting(string address);

    [LoggerMessage(EventId = 440, Level = LogLevel.Information, Message = "Machine {MachineId} continued run {RunId} from {Address} in {Environment}")]
    private partial void LogContinued(Guid machineId, Guid runId, string address, AgentEnvironment environment);

    [LoggerMessage(EventId = 441, Level = LogLevel.Warning, Message = "Refused the DDT service in Windows on machine {MachineId} from {Address}: it has no run to continue, so it removes itself")]
    private partial void LogNothingToContinue(Guid? machineId, string address);
}

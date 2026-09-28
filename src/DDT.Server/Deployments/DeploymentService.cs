// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Core.Boot;
using DDT.Core.Unattend;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Rules;
using DDT.Server.Security;
using DDT.Server.Sequences;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// Which run a machine has and what may happen to it, for the web endpoints, the agent endpoints and the registrar
// alike. Nothing here saves. The caller saves the change together with its audit rows, so the concurrency tokens on
// Machine.State, TokenGeneration and ActiveDeploymentId settle every race between an assignment, a pick, a cancel,
// a report and a registration. A caller that creates a run holds ImageStore.LibraryLock, so nothing the run
// downloads can be deleted between the lookup and the save.
public sealed class DeploymentService(
    DdtDbContext database,
    UserManager<DdtUser> users,
    SequenceCatalog catalog,
    SequenceResolver resolver,
    DdtSettings settings,
    TimeProvider timeProvider)
{
    // The agent runs in x64 WinPE and starts bcdboot from the applied image, which fails for any other image.
    public const string DeployableArchitecture = "x64";

    private const string SomeOperator = "an operator";

    public bool DomainConfigured => !string.IsNullOrWhiteSpace(settings.Current.Deployment.Domain.Name);

    // Under RequireWebApproval a netboot always waits for a sign-in, whatever networks are listed.
    public bool ZeroTouchEnabled => settings.Current.Machines.ZeroTouchEnabled;

    public async Task<Deployment?> ActiveAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return machine.ActiveDeploymentId is { } id
            ? await database.Deployments.FindAsync([id], cancellationToken).ConfigureAwait(false)
            : null;
    }

    // What the Machines page shows: the active run, else the one created last.
    public async Task<Deployment?> ShownAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return (machine.ActiveDeploymentId ?? machine.LastDeploymentId) is { } id
            ? await database.Deployments.FindAsync([id], cancellationToken).ConfigureAwait(false)
            : null;
    }

    // One query for the whole list, which loads one run per machine.
    public async Task<IReadOnlyDictionary<Guid, Deployment>> ShownForAsync(
        IReadOnlyCollection<Machine> machines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machines);

        List<Guid> ids = [.. machines.Select(m => m.ActiveDeploymentId ?? m.LastDeploymentId).OfType<Guid>().Distinct()];

        List<Deployment> deployments = ids.Count == 0
            ? []
            : await database.Deployments
                .AsNoTracking()
                .Where(d => ids.Contains(d.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        return deployments.ToDictionary(d => d.MachineId);
    }

    // Someone who may deploy signed in at this machine in its current token generation, and nothing is assigned.
    public async Task<bool> CanPickAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        cancellationToken.ThrowIfCancellationRequested();

        if (machine.State is not (MachineState.Approved or MachineState.Failed)
            || machine.SignedInByUserId is not { } userId
            || machine.ActiveDeploymentId is not null)
        {
            return false;
        }

        DdtUser? user = await users.FindByIdAsync(userId.ToString("D")).ConfigureAwait(false);

        return user is { IsDisabled: false }
            && !await users.IsLockedOutAsync(user).ConfigureAwait(false)
            && (await users.IsInRoleAsync(user, DdtRoleNames.Operator).ConfigureAwait(false)
                || await users.IsInRoleAsync(user, DdtRoleNames.Administrator).ConfigureAwait(false));
    }

    // The sequences a technician at the machine can choose: only those that can run, on this agent too, with what each
    // needs.
    public async Task<IReadOnlyList<AgentSequenceChoice>> ChoicesAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        List<TaskSequence> sequences = await database.TaskSequences.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        SequenceReferences references = await catalog.ReferencesAsync(cancellationToken).ConfigureAwait(false);
        Guid? suggested = await SuggestedAsync(machine, references, cancellationToken).ConfigureAwait(false);
        List<AgentSequenceChoice> choices = [];

        foreach (TaskSequence sequence in sequences.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id))
        {
            SequenceDefinition definition = SequenceDocuments.Read(sequence.Definition);

            if (definition.RequiredVersion() > machine.SequenceVersion || SequenceChecks.Check(definition, references).Problems.Count > 0)
            {
                continue;
            }

            IReadOnlyList<DeploymentArtifact> artifacts = RunSnapshots.Artifacts(Guid.Empty, definition, references, machine);

            choices.Add(new AgentSequenceChoice(
                sequence.Id,
                sequence.Name,
                sequence.Description,
                Erases(definition),
                SequenceChecks.ComputerNameUse(definition) is not null,
                RunSnapshots.RequiredBytes(definition, artifacts),
                sequence.Id == suggested,
                SequenceChecks.RawImage(definition, references)?.Name,
                SequenceChecks.RawImage(definition, references)?.BootCapability,
                SequenceChecks.RawImage(definition, references)?.SignedUnder));
        }

        return choices;
    }

    // The sequence the rules choose for the machine, offered first at the console. Only one that can run.
    public async Task<Guid?> SuggestedAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        SequenceReferences references = await catalog.ReferencesAsync(cancellationToken).ConfigureAwait(false);

        return await SuggestedAsync(machine, references, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DeploymentDecision> AssignAsync(
        Machine machine,
        AssignSequenceRequest request,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(request);

        ServerMessage? refusal = machine.State switch
        {
            MachineState.Deploying => ServerMessages.DeploymentMachineRunning.With(),
            MachineState.Rejected => ServerMessages.DeploymentMachineRejected.With(),
            MachineState.Retired => ServerMessages.DeploymentMachineRetired.With(),
            _ => null,
        };

        if (refusal is not null)
        {
            return DeploymentDecision.Conflict(refusal);
        }

        if (machine.ActiveDeploymentId is not null)
        {
            return DeploymentDecision.Conflict(ServerMessages.DeploymentAlreadyHasRun.With());
        }

        TaskSequence? sequence = await database.TaskSequences
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SequenceId, cancellationToken)
            .ConfigureAwait(false);

        if (sequence is null)
        {
            return DeploymentDecision.NotFound(ServerMessages.DeploymentSequenceGone.With());
        }

        (SequenceDefinition definition, SequenceReferences references, ServerMessage? problem) = await CheckAsync(sequence, cancellationToken)
            .ConfigureAwait(false);

        if (problem is not null)
        {
            return DeploymentDecision.Conflict(problem);
        }

        // Nobody at the machine can say which disk to erase. A machine too old to report its disks is let through,
        // and its agent refuses the run itself if it finds more than one.
        if (Erases(definition) && machine.EligibleDiskCount > 1)
        {
            return DeploymentDecision.Conflict(ServerMessages.DeploymentErasesOneOfManyDisks.With("sequence", sequence.Name));
        }

        if (ComputerNameProblem(machine, request.ComputerName, SequenceChecks.ComputerNameUse(definition)) is { } nameProblem)
        {
            return DeploymentDecision.Invalid("computerName", nameProblem);
        }

        (bool allowMismatch, ServerMessage? secureBootProblem) = SecureBootDecision(machine, definition, references, request.AllowSecureBootMismatch);

        if (secureBootProblem is not null)
        {
            return DeploymentDecision.Invalid("allowSecureBootMismatch", secureBootProblem);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Deployment deployment = Create(
            machine,
            sequence,
            definition,
            references,
            DeploymentSource.Web,
            ruleId: null,
            userId,
            userName,
            diskNumber: null,
            request.ComputerName,
            allowMismatch,
            now);

        database.AuditEvents.Add(Audit(
            AuditActions.DeploymentAssigned,
            deployment,
            now,
            address,
            $"{sequence.Name}, revision {sequence.Revision}, to machine {machine.Id:D}.{MismatchNote(machine, definition, references, allowMismatch)}",
            actorUserId: userId,
            actorName: userName));

        if (machine.State != MachineState.Pending)
        {
            // The tokens handed out with the Done report are of this generation, and Approved would accept them
            // again. The agent that held them rebooted, so nothing may use them any more.
            if (machine.State == MachineState.Done)
            {
                machine.TokenGeneration++;
            }

            machine.State = MachineState.Approved;
        }
        else if (AuthorizesWaitingMachine(machine, now))
        {
            machine.State = MachineState.Approved;
            machine.ApprovedByUserId = userId;
            machine.ApprovedUtc = now;
            machine.FirstApprovedUtc ??= now;

            database.AuditEvents.Add(new AuditEvent
            {
                OccurredUtc = now,
                Action = AuditActions.MachineApproved,
                ActorUserId = userId,
                ActorName = userName,
                SubjectId = machine.Id.ToString("D"),
                SourceAddress = address,
                Detail = StoredText.Bound($"Was Pending. Approved by assigning {sequence.Name}.", AuditEvent.MaxDetailLength),
            });
        }

        return DeploymentDecision.Accepted(deployment);
    }

    // The run of an approval on the web that took the sequence a rule chose. The approver saw that sequence, so the
    // rules must still choose it. The caller approves the machine: the rule alone never would.
    public async Task<DeploymentDecision> AssignByRuleAsync(
        Machine machine,
        Guid expectedSequenceId,
        bool allowSecureBootMismatch,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (machine.SignedInUserName is { } signer)
        {
            return DeploymentDecision.Conflict(ServerMessages.DeploymentSignerChooses.With("signer", signer));
        }

        SequenceResolution resolution = await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);

        if (resolution.Rule is not { } rule || resolution.Sequence is not { } sequence || sequence.Id != expectedSequenceId)
        {
            return DeploymentDecision.Conflict(ServerMessages.DeploymentRulesNoLongerChoose.With("explanation", resolution.Explanation));
        }

        (SequenceDefinition definition, SequenceReferences references, ServerMessage? problem) = await CheckAsync(sequence, cancellationToken)
            .ConfigureAwait(false);

        if (problem is not null)
        {
            return DeploymentDecision.Conflict(problem);
        }

        if (Erases(definition) && machine.EligibleDiskCount > 1)
        {
            return DeploymentDecision.Conflict(ServerMessages.DeploymentApproveThenChooseDisk.With("sequence", sequence.Name));
        }

        if (SequenceChecks.ComputerNameUse(definition) is not null && string.IsNullOrWhiteSpace(machine.AssignedName))
        {
            string use = definition.Steps.Any(step => step is JoinDomainStep) ? "domain" : "seed";

            return DeploymentDecision.Conflict(ServerMessages.DeploymentApproveThenName.With("sequence", sequence.Name, "use", use));
        }

        (bool allowMismatch, ServerMessage? secureBootProblem) = SecureBootDecision(machine, definition, references, allowSecureBootMismatch);

        if (secureBootProblem is not null)
        {
            return DeploymentDecision.Conflict(secureBootProblem);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Deployment deployment = Create(
            machine,
            sequence,
            definition,
            references,
            DeploymentSource.Rule,
            rule.Id,
            userId,
            userName,
            diskNumber: null,
            computerName: null,
            allowMismatch,
            now);

        database.AuditEvents.Add(Audit(
            AuditActions.DeploymentAssigned,
            deployment,
            now,
            address,
            $"{sequence.Name}, revision {sequence.Revision}, to machine {machine.Id:D}, chosen by rule {rule.Position + 1}, {rule.Name}, and approved by {userName ?? SomeOperator}.{MismatchNote(machine, definition, references, allowMismatch)}",
            actorUserId: userId,
            actorName: userName));

        return DeploymentDecision.Accepted(deployment);
    }

    // Chosen at the machine by whoever signed in there, who is also recorded as having requested it. A run of the
    // sequence a rule suggested keeps the rule, for the history.
    public async Task<DeploymentDecision> PickAsync(
        Machine machine,
        AgentRunRequest request,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(request);

        if (!await CanPickAsync(machine, cancellationToken).ConfigureAwait(false))
        {
            return DeploymentDecision.Conflict(machine.ActiveDeploymentId is null
                ? "Only an operator or administrator signed in at this machine can choose a sequence. Start the machine from the network again and sign in."
                : "This machine already has a run. It starts once the agent asks the server again.");
        }

        TaskSequence? sequence = await database.TaskSequences
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SequenceId, cancellationToken)
            .ConfigureAwait(false);

        if (sequence is null)
        {
            return DeploymentDecision.NotFound("The sequence no longer exists. Choose another sequence.");
        }

        (SequenceDefinition definition, SequenceReferences references, ServerMessage? problem) = await CheckAsync(sequence, cancellationToken)
            .ConfigureAwait(false);

        if (problem is not null)
        {
            return DeploymentDecision.Conflict(problem);
        }

        // The agent lists only what it can run, so this is a sequence changed after the list was shown.
        if (definition.RequiredVersion() > machine.SequenceVersion)
        {
            return DeploymentDecision.Conflict(
                $"{sequence.Name} needs a newer agent than this machine runs. Start the machine from the network again, so it gets the " +
                "server's agent, and choose it then.");
        }

        bool erases = Erases(definition);

        // The agent sends a disk for every sequence it listed as erasing one, once the technician typed ERASE. A pick
        // without one means the sequence was changed to erase a disk after the list was shown. The agent refuses such
        // a run itself, but only if the answer to this pick reached it and told it the run's id.
        if (erases && request.DiskNumber is null)
        {
            return DeploymentDecision.Conflict(
                $"{sequence.Name} erases a disk, and no disk was chosen for it at the machine. It was probably changed after the list was shown. Choose it again.");
        }

        if (erases && request.DiskNumber is < 0)
        {
            return DeploymentDecision.Invalid("diskNumber", "Choose one of the disks the agent listed.");
        }

        if (ComputerNameProblem(machine, request.ComputerName, SequenceChecks.ComputerNameUse(definition)) is { } nameProblem)
        {
            return DeploymentDecision.Invalid("computerName", nameProblem);
        }

        (bool allowMismatch, ServerMessage? secureBootProblem) = SecureBootDecision(machine, definition, references, request.AllowSecureBootMismatch);

        if (secureBootProblem is not null)
        {
            return DeploymentDecision.Invalid("allowSecureBootMismatch", secureBootProblem);
        }

        SequenceResolution resolution = await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = timeProvider.GetUtcNow();
        Deployment deployment = Create(
            machine,
            sequence,
            definition,
            references,
            DeploymentSource.Console,
            resolution.Sequence?.Id == sequence.Id ? resolution.Rule?.Id : null,
            machine.SignedInByUserId,
            machine.SignedInUserName,
            erases ? request.DiskNumber : null,
            request.ComputerName,
            allowMismatch,
            now);

        machine.State = MachineState.Approved;

        database.AuditEvents.Add(Audit(
            AuditActions.DeploymentAssigned,
            deployment,
            now,
            address,
            $"{sequence.Name}, revision {sequence.Revision}, to machine {machine.Id:D}, chosen at the machine.{MismatchNote(machine, definition, references, allowMismatch)}",
            actorUserId: machine.SignedInByUserId,
            actorName: machine.SignedInUserName,
            actorMachineId: machine.Id));

        return DeploymentDecision.Accepted(deployment);
    }

    // Null for an agent too old for the run's sequence, which would throw on a kind it does not know, and for the
    // service in Windows, which only ever continues a run that is running.
    public async Task<AgentRun?> HandOverAsync(Machine machine, Deployment run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);

        if (run.State is not (DeploymentState.Assigned or DeploymentState.Running)
            || (machine.AgentEnvironment == AgentEnvironment.Windows && run.State == DeploymentState.Assigned))
        {
            return null;
        }

        AgentRun handed = await AgentRunAsync(machine, run, cancellationToken).ConfigureAwait(false);

        return machine.SequenceVersion >= handed.Sequence.Version ? handed : null;
    }

    // The run as its agent receives it, from what was frozen when it was assigned.
    public async Task<AgentRun> AgentRunAsync(Machine machine, Deployment run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);

        DeploymentSnapshot snapshot = await database.DeploymentSnapshots
            .AsNoTracking()
            .SingleAsync(s => s.DeploymentId == run.Id, cancellationToken)
            .ConfigureAwait(false);

        List<DeploymentArtifact> artifacts = await database.DeploymentArtifacts
            .AsNoTracking()
            .Where(a => a.DeploymentId == run.Id)
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return RunSnapshots.ForAgent(run, SequenceDocuments.Read(snapshot.Definition), artifacts, machine.AssignedName);
    }

    // Cancels an assigned run, or stops a running one: the agent's next call is refused, and its resume token no
    // longer matches, so the machine starts over as Pending when it registers again.
    public async Task<DeploymentDecision> EndCurrentAsync(
        Machine machine,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        Deployment? active = await ActiveAsync(machine, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = timeProvider.GetUtcNow();
        string by = userName ?? SomeOperator;

        switch (active?.State)
        {
            case DeploymentState.Assigned:
                End(machine, active, DeploymentState.Cancelled, null, now);
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentCancelled,
                    active,
                    now,
                    address,
                    $"{active.Title} on machine {machine.Id:D}. Cancelled by {by}.",
                    actorUserId: userId,
                    actorName: userName));

                return DeploymentDecision.Accepted(active);

            case DeploymentState.Running:
                string error = $"Stopped by {by}.";
                await FailRunningStepsAsync(active, error, now, cancellationToken).ConfigureAwait(false);
                End(machine, active, DeploymentState.Failed, error, now);
                machine.State = MachineState.Failed;
                machine.TokenGeneration++;
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentFailed,
                    active,
                    now,
                    address,
                    $"{active.Title} on machine {machine.Id:D}. {error}",
                    actorUserId: userId,
                    actorName: userName));

                return DeploymentDecision.Accepted(active);

            default:
                return DeploymentDecision.Conflict(ServerMessages.DeploymentNothingToEnd.With());
        }
    }

    public async Task EndForRejectionAsync(
        Machine machine,
        Deployment? active,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        DateTimeOffset now = timeProvider.GetUtcNow();
        string by = userName ?? SomeOperator;

        switch (active?.State)
        {
            case DeploymentState.Assigned:
                End(machine, active, DeploymentState.Cancelled, null, now);
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentCancelled,
                    active,
                    now,
                    address,
                    $"{active.Title} on machine {machine.Id:D}. The machine was rejected by {by}.",
                    actorUserId: userId,
                    actorName: userName));
                break;

            case DeploymentState.Running:
                string error = $"Rejected by {by}.";
                await FailRunningStepsAsync(active, error, now, cancellationToken).ConfigureAwait(false);
                End(machine, active, DeploymentState.Failed, error, now);
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentFailed,
                    active,
                    now,
                    address,
                    $"{active.Title} on machine {machine.Id:D}. {error}",
                    actorUserId: userId,
                    actorName: userName));
                break;
        }

        machine.ActiveDeploymentId = null;
    }

    // Under RequireWebApproval, a sign-in at a machine an operator already assigned a sequence on the web completes
    // the approval: the assignment was the web half. A rule's run never counts, only a person's assignment.
    public static bool CountsAsWebApproval(Deployment? active) =>
        active is { State: DeploymentState.Assigned, Source: DeploymentSource.Web };

    // Zero touch: the web assignment authorizes the machine's next netboot, but only from a listed network. A request
    // still at a listed proxy's address carried no client address, so it proves nothing about the network.
    // The networks and the proxies come from one snapshot, so a save of either cannot fall between the two checks.
    public bool KeepsApprovalOnNetboot(Deployment? active, IPAddress? remoteAddress)
    {
        SettingsSnapshot snapshot = settings.Current;

        return CountsAsWebApproval(active)
            && snapshot.Machines.ZeroTouchEnabled
            && snapshot.Machines.ZeroTouchNetworks.Contains(remoteAddress)
            && !ListedProxies.Contains(snapshot, remoteAddress);
    }

    // A registration that does not continue the machine's run means the agent that had it is gone. A running run
    // fails. A run chosen at the machine or by a rule is cancelled: the disk and the ERASE typed there, and the
    // approval that took the rule's sequence, belonged to that boot. A web assignment stays for the next sign-in or a
    // zero touch netboot.
    public async Task EndForRestartAsync(
        Machine machine,
        Deployment? active,
        string? address,
        bool presentedRunToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        DateTimeOffset now = timeProvider.GetUtcNow();

        switch (active)
        {
            case { State: DeploymentState.Running }:
                string error = presentedRunToken
                    ? "The machine started again during the run, with a run token the server no longer accepts."
                    : "The machine started again during the run, without the run's token, so the run could not continue.";
                await FailRunningStepsAsync(active, error, now, cancellationToken).ConfigureAwait(false);
                End(machine, active, DeploymentState.Failed, error, now);
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentFailed,
                    active,
                    now,
                    address,
                    $"{active.Title} on machine {machine.Id:D}. {error}",
                    actorMachineId: machine.Id));
                break;

            case { State: DeploymentState.Assigned, Source: DeploymentSource.Console or DeploymentSource.Rule }:
                End(machine, active, DeploymentState.Cancelled, null, now);
                database.AuditEvents.Add(Audit(
                    AuditActions.DeploymentCancelled,
                    active,
                    now,
                    address,
                    $"{active.Title} on machine {machine.Id:D}. The machine started again before the run began.",
                    actorMachineId: machine.Id));
                break;
        }
    }

    // A running run whose agent has been silent for longer than a run token lasts, see AbandonedRunSweeper.
    public async Task EndForLostContactAsync(Machine machine, Deployment running, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(running);

        DateTimeOffset now = timeProvider.GetUtcNow();
        string error = $"The agent has not been in contact since {machine.LastSeenUtc:u} and could no longer resume the run.";

        await FailRunningStepsAsync(running, error, now, cancellationToken).ConfigureAwait(false);
        End(machine, running, DeploymentState.Failed, error, now);
        machine.State = MachineState.Failed;
        machine.TokenGeneration++;
        database.AuditEvents.Add(Audit(
            AuditActions.DeploymentFailed,
            running,
            now,
            address: null,
            $"{running.Title} on machine {machine.Id:D}. {error}"));
    }

    // A raw disk image whose boot file DDT could not read may still start, so only a known other processor keeps it
    // from being written.
    internal static ServerMessage? NotDeployable(Image image) => (image.Kind, image.Architecture) switch
    {
        (_, DeployableArchitecture) => null,
        (ImageKind.RawDisk, null) => null,
        (ImageKind.RawDisk, string architecture) =>
            ServerMessages.ImageRawForOtherArchitecture.With("image", image.Name, "architecture", architecture),
        (_, null) => ServerMessages.ImageWithoutArchitecture.With("image", image.Name),
        (_, string architecture) => ServerMessages.ImageOtherArchitecture.With("image", image.Name, "architecture", architecture),
    };

    private static bool Erases(SequenceDefinition definition) => definition.Steps.Any(step => step.ErasesDisk);

    // A sequence runs only without problems, which depend on the library and the settings of the moment.
    // While the stored deployment settings have problems, no run starts: every run would carry values nobody checked.
    // Runs that started already keep the values they started with.
    public static ServerMessage? SettingsProblem(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.DeploymentProblems.Count == 0
            ? null
            : ServerMessages.DeploymentSettingsHaveProblems.With(
                "problems",
                ServerMessages.Sentences([.. snapshot.DeploymentProblems.Select(problem => ServerMessages.SettingsFieldProblem.With(
                    "field",
                    SettingsDefinitions.Deployment.PageName(problem.Field),
                    "problem",
                    problem.Text))]));
    }

    private async Task<(SequenceDefinition Definition, SequenceReferences References, ServerMessage? Problem)> CheckAsync(
        TaskSequence sequence,
        CancellationToken cancellationToken)
    {
        SequenceReferences references = await catalog.ReferencesAsync(cancellationToken).ConfigureAwait(false);
        SequenceDefinition definition = SequenceDocuments.Read(sequence.Definition);

        ServerMessage? problem = SequenceChecks.Check(definition, references).Problems.Count switch
        {
            0 => SettingsProblem(settings.Current),
            int count => ServerMessages.DeploymentSequenceHasProblems.With("sequence", sequence.Name, "count", count),
        };

        return (definition, references, problem);
    }

    private async Task<Guid?> SuggestedAsync(Machine machine, SequenceReferences references, CancellationToken cancellationToken)
    {
        SequenceResolution resolution = await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);

        return resolution is { Rule: not null, Sequence: { } sequence }
            && SequenceChecks.Check(SequenceDocuments.Read(sequence.Definition), references).Problems.Count == 0
                ? sequence.Id
                : null;
    }

    // A raw disk image the machine will not start with Secure Boot on is written only where someone allowed it for the
    // run, or where the machine did not say Secure Boot is on; the agent checks the firmware again before it writes. An
    // image signed for Secure Boot is such an image on a machine whose firmware does not trust Microsoft's third-party
    // UEFI CA. The allowance is kept only where it matters. Returns it, and the refusal when the machine said Secure Boot
    // is on and nobody allowed the image.
    private static (bool Allow, ServerMessage? Problem) SecureBootDecision(
        Machine machine,
        SequenceDefinition definition,
        SequenceReferences references,
        bool allowed)
    {
        if (SequenceChecks.RawImage(definition, references) is not { } image || !NotStarting(machine, image))
        {
            return (false, null);
        }

        if (allowed)
        {
            return (true, null);
        }

        if (machine.SecureBootEnabled != true)
        {
            return (false, null);
        }

        return image.BootCapability == ImageBootCapability.SecureBootOk
            ? (false, ServerMessages.DeploymentUntrustedCaWithSecureBoot.With("image", image.Name, "ca", UefiCaName(image.SignedUnder)))
            : (false, ServerMessages.DeploymentNotStartingWithSecureBoot.With(
                "image",
                image.Name,
                "starting",
                BootCapabilities.NotStartingChoice(image.BootCapability)));
    }

    // Whether the machine would not start the image with Secure Boot on.
    private static bool NotStarting(Machine machine, Image image) =>
        image.BootCapability != ImageBootCapability.SecureBootOk || MicrosoftUefiCa.Untrusted(machine.TrustedUefiCas, image.SignedUnder);

    // MicrosoftUefiCa.Describe as a message, for a sentence the web says.
    private static ServerMessage UefiCaName(UefiCa? cas) => ServerMessages.MicrosoftUefiCaName.With("cas", cas switch
    {
        UefiCa.Microsoft2011 => "ca2011",
        UefiCa.Microsoft2023 => "ca2023",
        UefiCa.Microsoft2011 | UefiCa.Microsoft2023 => "both",
        _ => "other",
    });

    private static string MismatchNote(Machine machine, SequenceDefinition definition, SequenceReferences references, bool allowed)
    {
        if (!allowed || SequenceChecks.RawImage(definition, references) is not { } image || !NotStarting(machine, image))
        {
            return "";
        }

        return image.BootCapability == ImageBootCapability.SecureBootOk
            ? $" It may write {image.Name} although this machine's firmware does not trust {MicrosoftUefiCa.Describe(image.SignedUnder)}, which signed it."
            : $" It may write {image.Name} although it {BootCapabilities.NotStarting(image.BootCapability)} with Secure Boot on.";
    }

    // A machine joins the domain under its name, and a cloud-init seed may name it, so such a sequence needs a name.
    // Otherwise Windows setup or the image makes one up. use says why the sequence needs one, null when it needs none.
    private static ServerMessage? ComputerNameProblem(Machine machine, string? computerName, ServerMessage? use)
    {
        if (!string.IsNullOrWhiteSpace(computerName))
        {
            return ComputerNames.Problem(computerName.Trim());
        }

        return use is not null && string.IsNullOrWhiteSpace(machine.AssignedName)
            ? ServerMessages.DeploymentEnterComputerName.With("use", use)
            : null;
    }

    // Off: only a machine seen moments ago is at the prompt now; whoever holds the tokens of one seen earlier may
    // not be. On: the sign-in at the machine happened already, and the assignment is the web approval.
    private bool AuthorizesWaitingMachine(Machine machine, DateTimeOffset now) =>
        settings.Current.Machines.RequireWebApproval
            ? machine.SignedInByUserId is not null
            : now - machine.LastSeenUtc <= DeploymentLimits.WaitingAtPrompt;

    private Deployment Create(
        Machine machine,
        TaskSequence sequence,
        SequenceDefinition definition,
        SequenceReferences references,
        DeploymentSource source,
        Guid? ruleId,
        Guid? requestedByUserId,
        string? requestedByName,
        int? diskNumber,
        string? computerName,
        bool allowSecureBootMismatch,
        DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(computerName))
        {
            machine.AssignedName = computerName.Trim();
        }

        Deployment deployment = new()
        {
            Id = Guid.CreateVersion7(now),
            MachineId = machine.Id,
            TaskSequenceId = sequence.Id,
            SequenceRevision = sequence.Revision,
            RuleId = ruleId,
            Title = sequence.Name,
            DiskNumber = diskNumber,
            State = DeploymentState.Assigned,
            Source = source,
            RequestedByUserId = requestedByUserId,
            RequestedByName = requestedByName,
            StepCount = definition.Steps.Count,
            AllowSecureBootMismatch = allowSecureBootMismatch,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        database.Deployments.Add(deployment);
        database.DeploymentSnapshots.Add(new DeploymentSnapshot { DeploymentId = deployment.Id, Definition = sequence.Definition });
        database.DeploymentSteps.AddRange(RunSnapshots.Steps(deployment.Id, definition));
        database.DeploymentArtifacts.AddRange(RunSnapshots.Artifacts(deployment.Id, definition, references, machine));
        machine.ActiveDeploymentId = deployment.Id;
        machine.LastDeploymentId = deployment.Id;

        return deployment;
    }

    private async Task FailRunningStepsAsync(Deployment run, string error, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<DeploymentStep> running = await database.DeploymentSteps
            .Where(s => s.DeploymentId == run.Id && s.State == StepState.Running)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        RunReports.FailRunning(running, error, now);
    }

    private static void End(Machine machine, Deployment deployment, DeploymentState state, string? error, DateTimeOffset now)
    {
        deployment.State = state;
        deployment.Error = error;
        deployment.FinishedUtc = now;
        deployment.UpdatedUtc = now;
        machine.ActiveDeploymentId = null;
    }

    // A name in the detail is whatever an administrator typed, NUL included, which PostgreSQL refuses.
    private static AuditEvent Audit(
        string action,
        Deployment deployment,
        DateTimeOffset now,
        string? address,
        string detail,
        Guid? actorUserId = null,
        string? actorName = null,
        Guid? actorMachineId = null) => new()
        {
            OccurredUtc = now,
            Action = action,
            ActorUserId = actorUserId,
            ActorMachineId = actorMachineId,
            ActorName = actorName,
            SubjectId = deployment.Id.ToString("D"),
            SourceAddress = address,
            Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
        };
}

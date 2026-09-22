// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Core.Unattend;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Rules;
using DDT.Server.Security;
using DDT.Server.Sequences;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    IOptions<MachineOptions> machineOptions,
    IOptions<DeploymentOptions> deploymentOptions,
    ZeroTouchNetworks zeroTouchNetworks,
    ListedProxies listedProxies,
    TimeProvider timeProvider)
{
    // The agent runs in x64 WinPE and starts bcdboot from the applied image, which fails for any other image.
    public const string DeployableArchitecture = "x64";

    private const string SomeOperator = "an operator";

    public bool DomainConfigured => !string.IsNullOrWhiteSpace(deploymentOptions.Value.Domain.Name);

    // Under RequireWebApproval a netboot always waits for a sign-in, whatever networks are listed.
    public bool ZeroTouchEnabled => !machineOptions.Value.RequireWebApproval && !zeroTouchNetworks.IsEmpty;

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

    // The sequences a technician at the machine can choose: only those that can run, with what each needs.
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

            if (SequenceChecks.Check(definition, references).Problems.Count > 0)
            {
                continue;
            }

            IReadOnlyList<DeploymentArtifact> artifacts = RunSnapshots.Artifacts(Guid.Empty, definition, references, machine);

            choices.Add(new AgentSequenceChoice(
                sequence.Id,
                sequence.Name,
                sequence.Description,
                Erases(definition),
                JoinsDomain(definition),
                RunSnapshots.RequiredBytes(definition, artifacts),
                sequence.Id == suggested));
        }

        return choices;
    }

    // The sequence an assignment rule chooses for the machine, offered first at the console. Only one that can run.
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

        string? refusal = machine.State switch
        {
            MachineState.Deploying => "The machine is running a task sequence. Stop that run before assigning another sequence.",
            MachineState.Rejected => "The machine was rejected, so it cannot be given a sequence. Assign the sequence to another machine.",
            MachineState.Retired => "The machine was retired, so it cannot be given a sequence. Assign the sequence to another machine.",
            _ => null,
        };

        if (refusal is not null)
        {
            return DeploymentDecision.Conflict(refusal);
        }

        if (machine.ActiveDeploymentId is not null)
        {
            return DeploymentDecision.Conflict("This machine already has a run. Cancel it before assigning another sequence.");
        }

        TaskSequence? sequence = await database.TaskSequences
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SequenceId, cancellationToken)
            .ConfigureAwait(false);

        if (sequence is null)
        {
            return DeploymentDecision.NotFound("The sequence no longer exists. Load the page again and choose another sequence.");
        }

        (SequenceDefinition definition, SequenceReferences references, string? problem) = await CheckAsync(sequence, cancellationToken)
            .ConfigureAwait(false);

        if (problem is not null)
        {
            return DeploymentDecision.Conflict(problem);
        }

        // Nobody at the machine can say which disk to erase. A machine too old to report its disks is let through,
        // and its agent refuses the run itself if it finds more than one.
        if (Erases(definition) && machine.EligibleDiskCount > 1)
        {
            return DeploymentDecision.Conflict(
                $"{sequence.Name} erases a disk, and this machine has more than one. Sign in at it and choose the disk there.");
        }

        if (ComputerNameProblem(machine, request.ComputerName, JoinsDomain(definition)) is { } nameProblem)
        {
            return DeploymentDecision.Invalid("computerName", nameProblem);
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
            now);

        database.AuditEvents.Add(Audit(
            AuditActions.DeploymentAssigned,
            deployment,
            now,
            address,
            $"{sequence.Name}, revision {sequence.Revision}, to machine {machine.Id:D}.",
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
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (machine.SignedInUserName is { } signer)
        {
            return DeploymentDecision.Conflict($"{signer} signed in at the machine and chooses its sequence there. Approve it without a sequence.");
        }

        SequenceResolution resolution = await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);

        if (resolution.Rule is not { } rule || resolution.Sequence is not { } sequence || sequence.Id != expectedSequenceId)
        {
            return DeploymentDecision.Conflict(
                $"The rules no longer choose that sequence for this machine. {resolution.Explanation} Look at the machine again.");
        }

        (SequenceDefinition definition, SequenceReferences references, string? problem) = await CheckAsync(sequence, cancellationToken)
            .ConfigureAwait(false);

        if (problem is not null)
        {
            return DeploymentDecision.Conflict(problem);
        }

        if (Erases(definition) && machine.EligibleDiskCount > 1)
        {
            return DeploymentDecision.Conflict(
                $"{sequence.Name} erases a disk, and this machine has more than one. Approve it without a sequence, then sign in at it and choose the disk there.");
        }

        if (JoinsDomain(definition) && string.IsNullOrWhiteSpace(machine.AssignedName))
        {
            return DeploymentDecision.Conflict(
                $"{sequence.Name} joins the domain, and this machine has no name yet. Approve it without a sequence, then assign the sequence with a computer name.");
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
            now);

        database.AuditEvents.Add(Audit(
            AuditActions.DeploymentAssigned,
            deployment,
            now,
            address,
            $"{sequence.Name}, revision {sequence.Revision}, to machine {machine.Id:D}, chosen by the rule for {AssignmentRuleKeys.Describe(rule)} and approved by {userName ?? SomeOperator}.",
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

        (SequenceDefinition definition, SequenceReferences references, string? problem) = await CheckAsync(sequence, cancellationToken)
            .ConfigureAwait(false);

        if (problem is not null)
        {
            return DeploymentDecision.Conflict(problem);
        }

        bool erases = Erases(definition);

        if (erases && request.DiskNumber is < 0)
        {
            return DeploymentDecision.Invalid("diskNumber", "Choose one of the disks the agent listed.");
        }

        if (ComputerNameProblem(machine, request.ComputerName, JoinsDomain(definition)) is { } nameProblem)
        {
            return DeploymentDecision.Invalid("computerName", nameProblem);
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
            now);

        machine.State = MachineState.Approved;

        database.AuditEvents.Add(Audit(
            AuditActions.DeploymentAssigned,
            deployment,
            now,
            address,
            $"{sequence.Name}, revision {sequence.Revision}, to machine {machine.Id:D}, chosen at the machine.",
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
                return DeploymentDecision.Conflict(
                    "This machine has no run that is assigned or running. Load the page again to see its current state.");
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
    public bool KeepsApprovalOnNetboot(Deployment? active, IPAddress? remoteAddress) =>
        CountsAsWebApproval(active)
        && ZeroTouchEnabled
        && zeroTouchNetworks.Contains(remoteAddress)
        && !listedProxies.Contains(remoteAddress);

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

    internal static string? NotDeployable(Image image) => image.Architecture switch
    {
        DeployableArchitecture => null,
        null => $"{image.Name} does not say which processor it is for, and DDT deploys only x64 Windows. Choose an x64 image.",
        string architecture => $"{image.Name} is an {architecture} image, and DDT deploys only x64 Windows. Choose an x64 image.",
    };

    private static bool Erases(SequenceDefinition definition) => definition.Steps.Any(step => step.ErasesDisk);

    private static bool JoinsDomain(SequenceDefinition definition) => definition.Steps.Any(step => step is JoinDomainStep);

    // A sequence runs only without problems, which depend on the library and the settings of the moment.
    private async Task<(SequenceDefinition Definition, SequenceReferences References, string? Problem)> CheckAsync(
        TaskSequence sequence,
        CancellationToken cancellationToken)
    {
        SequenceReferences references = await catalog.ReferencesAsync(cancellationToken).ConfigureAwait(false);
        SequenceDefinition definition = SequenceDocuments.Read(sequence.Definition);

        string? problem = SequenceChecks.Check(definition, references).Problems.Count switch
        {
            0 => null,
            1 => $"{sequence.Name} has a problem, so it cannot run. Fix it on the sequence's page first.",
            int count => $"{sequence.Name} has {count} problems, so it cannot run. Fix them on the sequence's page first.",
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

    // A machine joins the domain under its name, so a sequence that joins one needs a name. Otherwise Setup makes
    // one up.
    private static string? ComputerNameProblem(Machine machine, string? computerName, bool joinsDomain)
    {
        if (!string.IsNullOrWhiteSpace(computerName))
        {
            return ComputerNames.IsValid(computerName.Trim(), out string error) ? null : error;
        }

        return joinsDomain && string.IsNullOrWhiteSpace(machine.AssignedName)
            ? "Enter a computer name. The sequence joins the machine to the domain under this name."
            : null;
    }

    // Off: only a machine seen moments ago is at the prompt now; whoever holds the tokens of one seen earlier may
    // not be. On: the sign-in at the machine happened already, and the assignment is the web approval.
    private bool AuthorizesWaitingMachine(Machine machine, DateTimeOffset now) =>
        machineOptions.Value.RequireWebApproval
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

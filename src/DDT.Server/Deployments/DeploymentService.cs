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
using DDT.Contracts.Values;
using DDT.Core.Boot;
using DDT.Core.Unattend;
using DDT.Core.Values;
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
    RunValues values,
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
        SequenceResolution resolution = await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);
        Guid? suggested = Suggested(resolution, references);
        DeploymentOptions deployment = settings.Current.Deployment;
        List<AgentSequenceChoice> choices = [];

        foreach (TaskSequence sequence in sequences.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id))
        {
            SequenceDefinition definition = SequenceDocuments.Read(sequence.Definition);

            if (definition.RequiredVersion() > machine.SequenceVersion || SequenceChecks.Check(definition, references).Problems.Count > 0)
            {
                continue;
            }

            IReadOnlyList<DeploymentArtifact> artifacts = RunSnapshots.Artifacts(Guid.Empty, definition, references, machine);

            // What the console asks after the pick, starting with what the machine, the rules and the defaults give.
            ValueResolution preview = ValueResolver.Resolve(MachineValues.Sources(machine, resolution.Machine, resolution.Match, definition, null, deployment));
            AgentInput[] inputs =
            [
                .. (definition.Inputs ?? [])
                    .OfType<InputDeclaration>()
                    .Where(input => input.AskAt is InputAsk.Machine or InputAsk.Both)
                    .Select(input => RunValues.Asked(input, preview)),
            ];

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
                SequenceChecks.RawImage(definition, references)?.SignedUnder,
                inputs.Length == 0 ? null : inputs));
        }

        return choices;
    }

    // The sequence the rules choose for the machine, offered first at the console. Only one that can run.
    public async Task<Guid?> SuggestedAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        SequenceReferences references = await catalog.ReferencesAsync(cancellationToken).ConfigureAwait(false);

        return Suggested(await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false), references);
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

        DateTimeOffset now = timeProvider.GetUtcNow();
        GivenAnswers given = await GivenAsync(machine, definition, request.Answers, atMachine: false, userName, request.ComputerName, null, now, cancellationToken)
            .ConfigureAwait(false);

        if (ComputerNameProblem(machine, request.ComputerName, SequenceChecks.ComputerNameUse(definition), given.Values) is { } nameProblem)
        {
            return DeploymentDecision.Invalid("computerName", nameProblem);
        }

        if (given.Problems.Count > 0)
        {
            return DeploymentDecision.InvalidAnswers(given.Problems);
        }

        (bool allowMismatch, ServerMessage? secureBootProblem) = SecureBootDecision(machine, definition, references, request.AllowSecureBootMismatch);

        if (secureBootProblem is not null)
        {
            return DeploymentDecision.Invalid("allowSecureBootMismatch", secureBootProblem);
        }

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
            given.Answers,
            now);

        if (await KeepAsync(deployment, definition, request.Answers, given, new RunCredentialGiver(userId, userName, false), address, cancellationToken)
            .ConfigureAwait(false) is { } kept)
        {
            return kept;
        }

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
        IReadOnlyList<InputAnswer>? answers,
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

        DateTimeOffset now = timeProvider.GetUtcNow();
        GivenAnswers given = await GivenAsync(machine, definition, answers, atMachine: false, userName, null, resolution, now, cancellationToken)
            .ConfigureAwait(false);

        // A rule's value such as PC-{{SerialNumber|alnum|right:8}} names every machine, so only a machine that nothing
        // names needs its name given with an assignment.
        if (SequenceChecks.ComputerNameUse(definition) is { } nameUse && string.IsNullOrWhiteSpace(machine.AssignedName) && !Named(given.Values))
        {
            return DeploymentDecision.Conflict(nameUse.Code == ServerMessages.SequenceNamesMachineWithValue.Code
                ? ServerMessages.DeploymentApproveThenNameValue.With("sequence", sequence.Name)
                : ServerMessages.DeploymentApproveThenName.With(
                    "sequence",
                    sequence.Name,
                    "use",
                    nameUse.Code == ServerMessages.DeploymentJoinsDomainUnderName.Code ? "domain" : "seed"));
        }

        if (given.Problems.Count > 0)
        {
            return DeploymentDecision.InvalidAnswers(given.Problems);
        }

        (bool allowMismatch, ServerMessage? secureBootProblem) = SecureBootDecision(machine, definition, references, allowSecureBootMismatch);

        if (secureBootProblem is not null)
        {
            return DeploymentDecision.Conflict(secureBootProblem);
        }

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
            given.Answers,
            now);

        if (await KeepAsync(deployment, definition, answers, given, new RunCredentialGiver(userId, userName, false), address, cancellationToken)
            .ConfigureAwait(false) is { } kept)
        {
            return kept;
        }

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

        SequenceResolution resolution = await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = timeProvider.GetUtcNow();
        GivenAnswers given = await GivenAsync(
                machine,
                definition,
                request.Answers,
                atMachine: true,
                machine.SignedInUserName,
                request.ComputerName,
                resolution,
                now,
                cancellationToken)
            .ConfigureAwait(false);

        if (ComputerNameProblem(machine, request.ComputerName, SequenceChecks.ComputerNameUse(definition), given.Values) is { } nameProblem)
        {
            return DeploymentDecision.Invalid("computerName", nameProblem);
        }

        if (given.Problems.Count > 0)
        {
            return DeploymentDecision.InvalidAnswers(given.Problems);
        }

        (bool allowMismatch, ServerMessage? secureBootProblem) = SecureBootDecision(machine, definition, references, request.AllowSecureBootMismatch);

        if (secureBootProblem is not null)
        {
            return DeploymentDecision.Invalid("allowSecureBootMismatch", secureBootProblem);
        }

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
            given.Answers,
            now);

        if (await KeepAsync(
                deployment,
                definition,
                request.Answers,
                given,
                new RunCredentialGiver(machine.SignedInByUserId, machine.SignedInUserName, true),
                address,
                cancellationToken)
            .ConfigureAwait(false) is { } kept)
        {
            return kept;
        }

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

        SequenceDefinition definition = SequenceDocuments.Read(snapshot.Definition);

        return RunSnapshots.ForAgent(
            run,
            definition,
            artifacts,
            machine.AssignedName,
            RunValues.Effective(run),
            await PendingInputsAsync(machine, run, definition, cancellationToken).ConfigureAwait(false));
    }

    // The inputs the machine asks before an assigned run can start: none unless a required one it asks has no answer,
    // and then every one it asks without an answer, so they are asked together. A run that started has its values.
    public async Task<IReadOnlyList<AgentInput>?> PendingInputsAsync(
        Machine machine,
        Deployment run,
        SequenceDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(definition);

        if (run.State != DeploymentState.Assigned || definition.Inputs is not { Count: > 0 })
        {
            return null;
        }

        RunValueCheck check = await values.CheckAsync(machine, run, definition, settings.Current.Deployment, cancellationToken).ConfigureAwait(false);

        return check.Missing.Any(input => input.AskAt is InputAsk.Machine or InputAsk.Both) ? check.AskedAtMachine : null;
    }

    // Answers to the inputs of an assigned run that lacks a required one, given on the machine's page or at the machine
    // while the run waits at its start; the machine answers only what it asks. The answers go with those the run has, and
    // the run no longer waits once no required input lacks an answer: the machine's agent starts it then. Before is the
    // run's answers as they were, over which the caller saves these, so that answers given elsewhere in the meantime win
    // and these are refused; Refusal is set when nothing waits for answers.
    public async Task<RunAnswering> AnswerAsync(
        Machine machine,
        Deployment run,
        IReadOnlyList<InputAnswer>? answers,
        bool atMachine,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);

        SequenceDefinition definition = await DefinitionAsync(run, cancellationToken).ConfigureAwait(false);
        DeploymentOptions deployment = settings.Current.Deployment;

        if (run.State != DeploymentState.Assigned)
        {
            return new RunAnswering(run.Answers, [], [], null, Refused: true);
        }

        RunValueCheck waiting = await values.CheckAsync(machine, run, definition, deployment, cancellationToken).ConfigureAwait(false);

        if (waiting.Missing.Count == 0)
        {
            return new RunAnswering(run.Answers, [], [], null, Refused: true);
        }

        List<AnswerProblem> problems = [.. RunValues.Check(definition, answers, input => !atMachine || input.AskAt is InputAsk.Machine or InputAsk.Both)];

        if (problems.Count > 0)
        {
            return new RunAnswering(run.Answers, waiting.AskedAtMachine, problems, null, Refused: false);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string? before = run.Answers;
        IReadOnlyList<RunAnswer> merged = RunValues.Merge(definition, RunAnswer.Read(run.Answers), answers, userName, atMachine, now);
        run.Answers = merged.Count == 0 ? null : RunAnswer.Write(merged);

        if (await values.KeepAccountsAsync(run, definition, answers, new RunCredentialGiver(userId, userName, atMachine), cancellationToken)
            .ConfigureAwait(false) is { } account)
        {
            return new RunAnswering(before, waiting.AskedAtMachine, [account], null, Refused: false);
        }

        RunValueCheck check = await values.CheckAsync(machine, run, definition, deployment, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> answered = RunValues.Answered(definition, answers);

        // A value an answer makes that cannot be used, such as a computer name Windows refuses, is the answer's to fix.
        problems.AddRange(check.Problems
            .Select(problem => (Problem: problem, Input: answered.FirstOrDefault(name => string.Equals(name, problem.Name, StringComparison.OrdinalIgnoreCase))))
            .Where(found => found.Input is not null)
            .Select(found => new AnswerProblem(found.Input!, $"{RunValues.AnswersField}.{found.Input}", found.Problem.Message)));

        if (problems.Count > 0)
        {
            return new RunAnswering(before, waiting.AskedAtMachine, problems, null, Refused: false);
        }

        run.InputsPending = check.Missing.Count > 0 && (atMachine || run.InputsPending);
        run.UpdatedUtc = now;
        AuditAnswers(run, answered, atMachine, now, address, userId, userName, atMachine ? machine.Id : null);

        return new RunAnswering(before, waiting.AskedAtMachine, [], check, Refused: false);
    }

    // Continues the pause the run waits at, the visit the page showed, so a click that comes late continues no later one.
    // The agent learns it with the answer to its next report, which comes within seconds while it waits.
    public async Task<DeploymentDecision> ContinueAsync(
        Machine machine,
        ContinueRunRequest request,
        Guid? userId,
        string? userName,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(request);

        Deployment? run = await ActiveAsync(machine, cancellationToken).ConfigureAwait(false);

        if (run is not { State: DeploymentState.Running, PauseStepId: { } stepId, PausePass: { } pass }
            || stepId != request.StepId
            || pass != request.Pass
            || DeploymentSummaries.Continued(run))
        {
            return DeploymentDecision.Conflict("The run no longer waits at that pause.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string? step = await database.DeploymentSteps
            .AsNoTracking()
            .Where(s => s.DeploymentId == run.Id && s.StepId == stepId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        run.ContinueStepId = stepId;
        run.ContinuePass = pass;
        run.ContinuedByName = StoredText.Bound(userName, 256);
        run.UpdatedUtc = now;
        database.AuditEvents.Add(Audit(
            AuditActions.DeploymentContinued,
            run,
            now,
            address,
            $"{run.Title} on machine {machine.Id:D}, at {step ?? "its pause"} ({stepId:D}), visit {pass}.",
            actorUserId: userId,
            actorName: userName));

        return DeploymentDecision.Accepted(run);
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

    // A step on any branch may run, so a sequence that erases a disk on one branch erases one.
    private static bool Erases(SequenceDefinition definition) => SequenceTree.Nodes(definition).Any(step => step.ErasesDisk);

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

    private static Guid? Suggested(SequenceResolution resolution, SequenceReferences references) =>
        resolution is { Rule: not null, Sequence: { } sequence }
            && SequenceChecks.Check(SequenceDocuments.Read(sequence.Definition), references).Problems.Count == 0
                ? sequence.Id
                : null;

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
    // Otherwise Windows setup or the image makes one up. use says why the sequence needs one, null when it needs none. A
    // name the run's values give, such as a rule's pattern, is as good as one given here.
    private static ServerMessage? ComputerNameProblem(Machine machine, string? computerName, ServerMessage? use, ValueResolution values)
    {
        if (!string.IsNullOrWhiteSpace(computerName))
        {
            return ComputerNames.Problem(computerName.Trim());
        }

        return use is not null && string.IsNullOrWhiteSpace(machine.AssignedName) && !Named(values)
            ? ServerMessages.DeploymentEnterComputerName.With("use", use)
            : null;
    }

    // The values give the machine a name Windows takes.
    private static bool Named(ValueResolution values) =>
        values.Effective.TryGetValue(MachineVariableNames.ComputerName, out string? name)
        && !string.IsNullOrWhiteSpace(name)
        && !values.Problems.Any(problem => string.Equals(problem.Name, MachineVariableNames.ComputerName, StringComparison.OrdinalIgnoreCase));

    // The answers given with an assignment, an approval or a pick, checked against the inputs asked there, and the values
    // they make with the rules as they are now. An input only asked there must be answered unless a value or a default
    // answers it; one asked in both places may be left for the other, and the run then waits for it. A value an answer
    // makes that cannot be used is the answer's problem. ComputerName is the name the request gives the machine.
    private async Task<GivenAnswers> GivenAsync(
        Machine machine,
        SequenceDefinition definition,
        IReadOnlyList<InputAnswer>? answers,
        bool atMachine,
        string? answeredBy,
        string? computerName,
        SequenceResolution? resolution,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        InputAsk only = atMachine ? InputAsk.Machine : InputAsk.Web;
        List<AnswerProblem> problems = [.. RunValues.Check(definition, answers, input => input.AskAt == only || input.AskAt == InputAsk.Both)];
        IReadOnlyList<RunAnswer> kept = problems.Count == 0 ? RunValues.Merge(definition, [], answers, answeredBy, atMachine, now) : [];
        IReadOnlyList<string> answered = problems.Count == 0 ? RunValues.Answered(definition, answers) : [];
        IReadOnlyDictionary<string, string> byName = MachineValues.Answers(kept);

        resolution ??= await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);
        ValueSources sources = MachineValues.Sources(machine, resolution.Machine, resolution.Match, definition, byName, settings.Current.Deployment);

        if (!string.IsNullOrWhiteSpace(computerName))
        {
            sources = sources with { Machine = [new NamedValue(MachineVariableNames.ComputerName, computerName.Trim())] };
        }

        ValueResolution resolved = ValueResolver.Resolve(sources);

        if (problems.Count > 0)
        {
            return new GivenAnswers([], [], resolved, problems);
        }

        RunValueCheck check = RunValues.Check(definition, resolved, byName, answered.ToHashSet(StringComparer.OrdinalIgnoreCase));

        problems.AddRange(check.Missing
            .Where(input => input.AskAt == only)
            .Select(input => new AnswerProblem(input.Name, $"{RunValues.AnswersField}.{input.Name}", ServerMessages.ValuesInputRequired.With("label", input.Label))));
        problems.AddRange(check.Problems
            .Select(problem => (Problem: problem, Input: answered.FirstOrDefault(name => string.Equals(name, problem.Name, StringComparison.OrdinalIgnoreCase))))
            .Where(found => found.Input is not null)
            .Select(found => new AnswerProblem(found.Input!, $"{RunValues.AnswersField}.{found.Input}", found.Problem.Message)));

        return new GivenAnswers(kept, answered, resolved, problems);
    }

    // Keeps the Account answers for the new run and audits the answers by name. Null when all are kept.
    private async Task<DeploymentDecision?> KeepAsync(
        Deployment run,
        SequenceDefinition definition,
        IReadOnlyList<InputAnswer>? answers,
        GivenAnswers given,
        RunCredentialGiver giver,
        string? address,
        CancellationToken cancellationToken)
    {
        if (await values.KeepAccountsAsync(run, definition, answers, giver, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return DeploymentDecision.InvalidAnswers([problem]);
        }

        AuditAnswers(run, given.Answered, giver.AtMachine, run.CreatedUtc, address, giver.UserId, giver.Name, giver.AtMachine ? run.MachineId : null);

        return null;
    }

    // Names only: an answer can be anything a person typed, and an Account input's is a password.
    private void AuditAnswers(
        Deployment run,
        IReadOnlyList<string> answered,
        bool atMachine,
        DateTimeOffset now,
        string? address,
        Guid? userId,
        string? userName,
        Guid? machineId)
    {
        if (answered.Count == 0)
        {
            return;
        }

        database.AuditEvents.Add(Audit(
            AuditActions.DeploymentInputsAnswered,
            run,
            now,
            address,
            $"{string.Join(", ", answered)} of {run.Title} on machine {run.MachineId:D}, answered {(atMachine ? "at the machine" : "on the web")}.",
            actorUserId: userId,
            actorName: userName,
            actorMachineId: machineId));
    }

    private async Task<SequenceDefinition> DefinitionAsync(Deployment run, CancellationToken cancellationToken)
    {
        DeploymentSnapshot snapshot = await database.DeploymentSnapshots
            .AsNoTracking()
            .SingleAsync(s => s.DeploymentId == run.Id, cancellationToken)
            .ConfigureAwait(false);

        return SequenceDocuments.Read(snapshot.Definition);
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
        IReadOnlyList<RunAnswer> answers,
        DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(computerName))
        {
            machine.AssignedName = computerName.Trim();
        }

        Guid id = Guid.CreateVersion7(now);
        IReadOnlyList<DeploymentStep> steps = RunSnapshots.Steps(id, definition);
        Deployment deployment = new()
        {
            Id = id,
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
            StepCount = RunPaths.Leaves(definition, steps).Count,
            AllowSecureBootMismatch = allowSecureBootMismatch,
            Answers = answers.Count == 0 ? null : RunAnswer.Write(answers),
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        database.Deployments.Add(deployment);
        database.DeploymentSnapshots.Add(new DeploymentSnapshot { DeploymentId = deployment.Id, Definition = sequence.Definition });
        database.DeploymentSteps.AddRange(steps);
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

// The answers given with an assignment, an approval or a pick: those to keep as the run's, the inputs they answer,
// Account inputs included, the values they make, and their problems.
internal sealed record GivenAnswers(
    IReadOnlyList<RunAnswer> Answers,
    IReadOnlyList<string> Answered,
    ValueResolution Values,
    IReadOnlyList<AnswerProblem> Problems);

// What answers to a waiting run came to. Refused: the run waits for no answers. Before is the run's answers as they
// were and Asked what the machine asked then, Problems what is wrong with these, and Check the run's values with them.
public sealed record RunAnswering(
    string? Before,
    IReadOnlyList<AgentInput> Asked,
    IReadOnlyList<AnswerProblem> Problems,
    RunValueCheck? Check,
    bool Refused);

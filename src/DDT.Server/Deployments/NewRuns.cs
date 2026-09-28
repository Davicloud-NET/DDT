// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Unattend;
using DDT.Core.Values;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Rules;
using DDT.Server.Sequences;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// What an assignment, an approval and a pick share. Nothing here saves; a caller that creates a run holds
// ImageStore.LibraryLock, so nothing the run downloads can be deleted between the lookup and the save.
public sealed class NewRuns(
    DdtDbContext database,
    SequenceCatalog catalog,
    SequenceResolver resolver,
    RunValues values,
    DdtSettings settings)
{
    public Task<TaskSequence?> LoadAsync(Guid sequenceId, CancellationToken cancellationToken) =>
        database.TaskSequences.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sequenceId, cancellationToken);

    // A sequence runs only without problems, which depend on the library and the settings of the moment.
    public async Task<CheckedSequence> CheckAsync(TaskSequence sequence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        SequenceReferences references = await catalog.ReferencesAsync(cancellationToken).ConfigureAwait(false);
        SequenceDefinition definition = SequenceDocuments.Read(sequence.Definition);

        ServerMessage? problem = SequenceChecks.Check(definition, references).Problems.Count switch
        {
            0 => DeploymentPolicy.SettingsProblem(settings.Current),
            int count => ServerMessages.DeploymentSequenceHasProblems.With("sequence", sequence.Name, "count", count),
        };

        return new CheckedSequence(sequence, definition, references, problem);
    }

    // An input asked only where the answers were given needs one unless a value or a default answers it; one asked in both
    // places may be left for the other, and the run then waits for it.
    public async Task<GivenAnswers> GivenAsync(RunRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        (Machine machine, SequenceDefinition definition, bool atMachine) = (request.Machine, request.Sequence.Definition, request.Giver.AtMachine);
        InputAsk only = atMachine ? InputAsk.Machine : InputAsk.Web;
        List<AnswerProblem> problems = [.. RunValues.Check(definition, request.Answers, input => input.AskAt == only || input.AskAt == InputAsk.Both)];
        IReadOnlyList<RunAnswer> kept = problems.Count == 0
            ? RunValues.Merge(definition, [], request.Answers, new RunAnswerGiver(request.Giver.Name, atMachine, now))
            : [];
        IReadOnlyList<string> answered = problems.Count == 0 ? RunValues.Answered(definition, request.Answers) : [];
        IReadOnlyDictionary<string, string> byName = MachineValues.Answers(kept);

        SequenceResolution resolution = request.Resolution ?? await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);
        ValueSources sources = MachineValues.Sources(machine, resolution, definition, byName, settings.Current.Deployment);

        if (!string.IsNullOrWhiteSpace(request.ComputerName))
        {
            sources = sources with { Machine = [new NamedValue(MachineVariableNames.ComputerName, request.ComputerName.Trim())] };
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
        problems.AddRange(RunValues.AnsweredValueProblems(check, answered));

        return new GivenAnswers(kept, answered, resolved, problems);
    }

    // The computer name, the answers and the Secure Boot allowance, refused by field as the web and the console ask them
    // again. AllowMismatch is what the run keeps of the allowance.
    public static (bool AllowMismatch, DeploymentDecision? Refusal) Validate(RunRequest request, GivenAnswers given)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(given);

        (Machine machine, CheckedSequence sequence) = (request.Machine, request.Sequence);

        if (ComputerNameProblem(machine, request.ComputerName, SequenceChecks.ComputerNameUse(sequence.Definition), given.Values) is { } nameProblem)
        {
            return (false, DeploymentDecision.Invalid("computerName", nameProblem));
        }

        if (given.Problems.Count > 0)
        {
            return (false, DeploymentDecision.InvalidAnswers(given.Problems));
        }

        (bool allowMismatch, ServerMessage? secureBootProblem) = SecureBootPolicy.Decide(
            machine,
            sequence.Definition,
            sequence.References,
            request.AllowSecureBootMismatch);

        return secureBootProblem is null
            ? (allowMismatch, null)
            : (false, DeploymentDecision.Invalid("allowSecureBootMismatch", secureBootProblem));
    }

    // Creates the run, adds the DeploymentInputsAnswered audit and keeps the Account answers; the refusal of the first
    // account that cannot be kept. A refused run stays tracked, so the caller must not save.
    public async Task<(Deployment Run, DeploymentDecision? Refusal)> CreateAsync(NewRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        RunRequest request = run.Request;
        Deployment deployment = Create(run);

        if (await values.KeepAccountsAsync(deployment, request.Sequence.Definition, request.Answers, request.Giver, cancellationToken)
            .ConfigureAwait(false) is { } problem)
        {
            return (deployment, DeploymentDecision.InvalidAnswers([problem]));
        }

        RunCredentialGiver giver = request.Giver;
        Actor answeredBy = new(giver.UserId, giver.Name, request.Address, giver.AtMachine ? deployment.MachineId : null);

        if (RunAnswerAudits.Of(deployment, run.Given.Answered, giver.AtMachine, deployment.CreatedUtc, answeredBy) is { } audit)
        {
            database.AuditEvents.Add(audit);
        }

        return (deployment, null);
    }

    // Windows setup or the image makes up a name, except where the sequence needs one: it joins the domain under it, or a
    // cloud-init seed names the machine. A name the run's values give, such as a rule's pattern, is as good as one given here.
    private static ServerMessage? ComputerNameProblem(Machine machine, string? computerName, ServerMessage? use, ValueResolution values)
    {
        if (!string.IsNullOrWhiteSpace(computerName))
        {
            return ComputerNames.Problem(computerName.Trim());
        }

        return use is not null && string.IsNullOrWhiteSpace(machine.AssignedName) && !RunValues.NamesMachine(values)
            ? ServerMessages.DeploymentEnterComputerName.With("use", use)
            : null;
    }

    private Deployment Create(NewRun run)
    {
        (Machine machine, CheckedSequence sequence) = (run.Request.Machine, run.Request.Sequence);

        if (!string.IsNullOrWhiteSpace(run.Request.ComputerName))
        {
            machine.AssignedName = run.Request.ComputerName.Trim();
        }

        Guid id = Guid.CreateVersion7(run.Now);
        IReadOnlyList<DeploymentStep> steps = RunSnapshots.Steps(id, sequence.Definition);
        Deployment deployment = new()
        {
            Id = id,
            MachineId = machine.Id,
            TaskSequenceId = sequence.Sequence.Id,
            SequenceRevision = sequence.Sequence.Revision,
            RuleId = run.RuleId,
            Title = sequence.Sequence.Name,
            DiskNumber = run.DiskNumber,
            State = DeploymentState.Assigned,
            Source = run.Source,
            RequestedByUserId = run.Request.Giver.UserId,
            RequestedByName = run.Request.Giver.Name,
            StepCount = RunPaths.Leaves(sequence.Definition, steps).Count,
            AllowSecureBootMismatch = run.AllowSecureBootMismatch,
            Answers = run.Given.Answers.Count == 0 ? null : RunAnswer.Write(run.Given.Answers),
            CreatedUtc = run.Now,
            UpdatedUtc = run.Now,
        };

        database.Deployments.Add(deployment);
        database.DeploymentSnapshots.Add(new DeploymentSnapshot { DeploymentId = deployment.Id, Definition = sequence.Sequence.Definition });
        database.DeploymentSteps.AddRange(steps);
        database.DeploymentArtifacts.AddRange(RunSnapshots.Artifacts(deployment.Id, sequence.Definition, sequence.References, machine));
        machine.ActiveDeploymentId = deployment.Id;
        machine.LastDeploymentId = deployment.Id;

        return deployment;
    }
}

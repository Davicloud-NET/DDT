// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Rules;
using DDT.Server.Sequences;
using DDT.Server.Settings;

namespace DDT.Server.Deployments;

// A run an operator gives a machine on the web: an assignment, or an approval that takes the sequence a rule chose.
// Nothing here saves. The caller saves the run with its audit rows.
public sealed class RunAssignments(
    DdtDbContext database,
    NewRuns newRuns,
    SequenceResolver resolver,
    DdtSettings settings,
    TimeProvider timeProvider)
{
    private const string SomeOperator = "an operator";

    public async Task<DeploymentDecision> AssignAsync(
        Machine machine,
        AssignSequenceRequest request,
        Actor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        if (Refusal(machine) is { } refusal)
        {
            return refusal;
        }

        TaskSequence? sequence = await newRuns.LoadAsync(request.SequenceId, cancellationToken).ConfigureAwait(false);

        if (sequence is null)
        {
            return DeploymentDecision.NotFound(ServerMessages.DeploymentSequenceGone.With());
        }

        CheckedSequence checkedSequence = await newRuns.CheckAsync(sequence, cancellationToken).ConfigureAwait(false);

        if (checkedSequence.Problem is { } problem)
        {
            return DeploymentDecision.Conflict(problem);
        }

        // Nobody at the machine can say which disk to erase. A machine too old to report its disks is let through, and
        // its agent refuses the run itself if it finds more than one.
        if (SequenceChecks.Erases(checkedSequence.Definition) && machine.EligibleDiskCount > 1)
        {
            return DeploymentDecision.Conflict(ServerMessages.DeploymentErasesOneOfManyDisks.With("sequence", sequence.Name));
        }

        RunRequest run = new(
            machine,
            checkedSequence,
            request.Answers,
            request.ComputerName,
            request.AllowSecureBootMismatch,
            new RunCredentialGiver(actor.UserId, actor.Name, false),
            actor.Address);

        return await CreateAsync(run, actor, cancellationToken).ConfigureAwait(false);
    }

    // Approves a waiting machine to run the sequence a rule chose. The approver saw that sequence, so the rules must
    // still choose it. The rule alone never approves a machine.
    public async Task<DeploymentDecision> ApproveByRuleAsync(
        Machine machine,
        ApproveMachineRequest request,
        Actor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        if (machine.SignedInUserName is { } signer)
        {
            return DeploymentDecision.Conflict(ServerMessages.DeploymentSignerChooses.With("signer", signer));
        }

        SequenceResolution resolution = await resolver.ResolveAsync(machine, cancellationToken).ConfigureAwait(false);

        if (resolution.Rule is not { } rule || resolution.Sequence is not { } sequence || sequence.Id != request.ExpectedSequenceId)
        {
            return DeploymentDecision.Conflict(ServerMessages.DeploymentRulesNoLongerChoose.With("explanation", resolution.Explanation));
        }

        CheckedSequence checkedSequence = await newRuns.CheckAsync(sequence, cancellationToken).ConfigureAwait(false);

        if (checkedSequence.Problem is { } problem)
        {
            return DeploymentDecision.Conflict(problem);
        }

        if (SequenceChecks.Erases(checkedSequence.Definition) && machine.EligibleDiskCount > 1)
        {
            return DeploymentDecision.Conflict(ServerMessages.DeploymentApproveThenChooseDisk.With("sequence", sequence.Name));
        }

        RunRequest run = new(
            machine,
            checkedSequence,
            request.Answers,
            null,
            request.AllowSecureBootMismatch,
            new RunCredentialGiver(actor.UserId, actor.Name, false),
            actor.Address)
        {
            Resolution = resolution,
        };

        return await CreateByRuleAsync(run, rule, actor, cancellationToken).ConfigureAwait(false);
    }

    private static DeploymentDecision? Refusal(Machine machine)
    {
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

        return machine.ActiveDeploymentId is null ? null : DeploymentDecision.Conflict(ServerMessages.DeploymentAlreadyHasRun.With());
    }

    // A rule's value such as PC-{{SerialNumber|alnum|right:8}} names every machine. So only a machine that nothing
    // names needs a name given with an assignment, and an approval can't give one.
    private static DeploymentDecision? ByRuleRefusal(RunRequest run, GivenAnswers given)
    {
        (Machine machine, CheckedSequence sequence) = (run.Machine, run.Sequence);

        if (SequenceChecks.ComputerNameUse(sequence.Definition) is { } nameUse
            && string.IsNullOrWhiteSpace(machine.AssignedName)
            && !RunValues.NamesMachine(given.Values))
        {
            return DeploymentDecision.Conflict(nameUse.Code == ServerMessages.SequenceNamesMachineWithValue.Code
                ? ServerMessages.DeploymentApproveThenNameValue.With("sequence", sequence.Sequence.Name)
                : ServerMessages.DeploymentApproveThenName.With(
                    "sequence",
                    sequence.Sequence.Name,
                    "use",
                    nameUse.Code == ServerMessages.DeploymentJoinsDomainUnderName.Code ? "domain" : "seed"));
        }

        return given.Problems.Count > 0 ? DeploymentDecision.InvalidAnswers(given.Problems) : null;
    }

    private async Task<DeploymentDecision> CreateAsync(RunRequest request, Actor actor, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        GivenAnswers given = await newRuns.GivenAsync(request, now, cancellationToken).ConfigureAwait(false);
        (bool allowMismatch, DeploymentDecision? invalid) = NewRuns.Validate(request, given);

        if (invalid is not null)
        {
            return invalid;
        }

        (Deployment deployment, DeploymentDecision? refused) = await newRuns
            .CreateAsync(new NewRun(request, DeploymentSource.Web, given, now) { AllowSecureBootMismatch = allowMismatch }, cancellationToken)
            .ConfigureAwait(false);

        if (refused is not null)
        {
            return refused;
        }

        (Machine machine, CheckedSequence sequence) = (request.Machine, request.Sequence);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.DeploymentAssigned,
            deployment.Id.ToString("D"),
            actor,
            now,
            $"{sequence.Sequence.Name}, revision {sequence.Sequence.Revision}, to machine {machine.Id:D}."
                + SecureBootPolicy.MismatchNote(machine, sequence.Definition, sequence.References, allowMismatch)));
        ApproveAsAssigned(machine, sequence.Sequence, actor, now);

        return DeploymentDecision.Accepted(deployment);
    }

    private async Task<DeploymentDecision> CreateByRuleAsync(RunRequest request, Rule rule, Actor actor, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        GivenAnswers given = await newRuns.GivenAsync(request, now, cancellationToken).ConfigureAwait(false);

        if (ByRuleRefusal(request, given) is { } refusal)
        {
            return refusal;
        }

        (Machine machine, CheckedSequence sequence) = (request.Machine, request.Sequence);
        (bool allowMismatch, ServerMessage? secureBootProblem) = SecureBootPolicy.Decide(
            machine,
            sequence.Definition,
            sequence.References,
            request.AllowSecureBootMismatch);

        if (secureBootProblem is not null)
        {
            return DeploymentDecision.Conflict(secureBootProblem);
        }

        NewRun run = new(request, DeploymentSource.Rule, given, now) { RuleId = rule.Id, AllowSecureBootMismatch = allowMismatch };
        (Deployment deployment, DeploymentDecision? refused) = await newRuns.CreateAsync(run, cancellationToken).ConfigureAwait(false);

        if (refused is not null)
        {
            return refused;
        }

        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.DeploymentAssigned,
            deployment.Id.ToString("D"),
            actor,
            now,
            $"{sequence.Sequence.Name}, revision {sequence.Sequence.Revision}, to machine {machine.Id:D}, chosen by rule {rule.Position + 1}, "
                + $"{rule.Name}, and approved by {actor.Name ?? SomeOperator}."
                + SecureBootPolicy.MismatchNote(machine, sequence.Definition, sequence.References, allowMismatch)));

        DateTimeOffset approved = timeProvider.GetUtcNow();
        machine.Approve(actor.UserId, approved);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.MachineApproved,
            machine.Id.ToString("D"),
            actor,
            approved,
            $"Was Pending. Approved to run {deployment.Title}, which a rule chose."));

        return DeploymentDecision.Accepted(deployment);
    }

    // An assignment only approves a waiting machine if DeploymentPolicy says it authorizes one.
    private void ApproveAsAssigned(Machine machine, TaskSequence sequence, Actor actor, DateTimeOffset now)
    {
        if (machine.State != MachineState.Pending)
        {
            // The tokens handed out with the Done report are of this generation, and Approved would accept them again.
            // The agent that held them rebooted, so nothing may use them any more.
            if (machine.State == MachineState.Done)
            {
                machine.TokenGeneration++;
            }

            machine.State = MachineState.Approved;
        }
        else if (DeploymentPolicy.AuthorizesWaitingMachine(settings.Current, machine, now))
        {
            machine.Approve(actor.UserId, now);
            database.AuditEvents.Add(AuditEvents.Create(
                AuditActions.MachineApproved,
                machine.Id.ToString("D"),
                actor,
                now,
                $"Was Pending. Approved by assigning {sequence.Name}."));
        }
    }
}

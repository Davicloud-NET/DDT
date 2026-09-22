// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DDT.Server.Deployments;

// The passwords a run needs, handed to its agent just in time: only for the step that needs them, only while that
// step runs, and every read audited. They are read from the configuration now and never stored with the run.
// Nothing here logs them. The caller saves the audit row before it answers.
public sealed class RunSecrets(
    DdtDbContext database,
    UnattendRenderer renderer,
    IOptions<DeploymentOptions> options,
    TimeProvider timeProvider)
{
    public async Task<(string? AnswerFile, string? Refusal)> AnswerFileAsync(
        Machine machine,
        Guid runId,
        Guid stepId,
        string? address,
        CancellationToken cancellationToken)
    {
        (Deployment? run, SequenceStep? step, RunInputs? inputs, string? refusal) = await RunningStepAsync(machine, runId, stepId, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return (null, refusal);
        }

        if (step is not WriteUnattendStep unattend)
        {
            return (null, "That step writes no answer file.");
        }

        if (unattend.LocalAdministrator && string.IsNullOrEmpty(options.Value.LocalAdministrator.Password))
        {
            return (null, "The step adds the local administrator, but DDT:Deployment:LocalAdministrator has no password any more.");
        }

        string? language = await database.DeploymentArtifacts
            .AsNoTracking()
            .Where(a => a.DeploymentId == runId && a.Kind == ArtifactKind.Image)
            .Select(a => a.Language)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        Audit(run!, machine, address, $"The answer file of step {step.Name} ({stepId:D}) of {run!.Title}.");

        return (renderer.Render(inputs!, unattend, language), null);
    }

    // The domain is the one configured when the run started: a domain named anywhere else could send the join
    // account to a foreign domain controller. The join runs in Windows, so only the service there gets it.
    public async Task<(AgentJoinDomainCredentials? Credentials, string? Refusal)> JoinCredentialsAsync(
        Machine machine,
        Guid runId,
        Guid stepId,
        string? address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (machine.AgentEnvironment != AgentEnvironment.Windows)
        {
            return (null, "A machine joins its domain in Windows, and this agent registered from Windows PE.");
        }

        (Deployment? run, SequenceStep? step, RunInputs? inputs, string? refusal) = await RunningStepAsync(machine, runId, stepId, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return (null, refusal);
        }

        if (step is not JoinDomainStep join)
        {
            return (null, "That step joins no domain.");
        }

        DomainOptions domain = options.Value.Domain;

        if (string.IsNullOrWhiteSpace(domain.UserName) || string.IsNullOrEmpty(domain.Password))
        {
            return (null, "DDT:Deployment:Domain no longer names an account to join the domain with.");
        }

        if (inputs!.DomainName is not { } name || !string.Equals(name, domain.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (null, "The configured domain changed after the run started, so its account is not for this run. Assign the sequence again.");
        }

        Audit(run!, machine, address, $"The domain join credentials of step {step.Name} ({stepId:D}) of {run!.Title}, for {name}.");

        string? organizationalUnit = string.IsNullOrWhiteSpace(join.OrganizationalUnit) ? inputs.DomainOrganizationalUnit : join.OrganizationalUnit.Trim();

        return (new AgentJoinDomainCredentials(name, organizationalUnit, domain.UserName.Trim(), domain.Password), null);
    }

    // The step, as frozen with the run, when the machine's active run is running it.
    private async Task<(Deployment? Run, SequenceStep? Step, RunInputs? Inputs, string? Refusal)> RunningStepAsync(
        Machine machine,
        Guid runId,
        Guid stepId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        Deployment? run = machine.ActiveDeploymentId == runId
            ? await database.Deployments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == runId, cancellationToken).ConfigureAwait(false)
            : null;

        if (run is not { State: DeploymentState.Running, Inputs: { } inputs })
        {
            return (null, null, null, "This machine has no such run that is running. Report the run as running first.");
        }

        DeploymentStep? row = await database.DeploymentSteps
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.DeploymentId == runId && s.StepId == stepId, cancellationToken)
            .ConfigureAwait(false);

        if (row is not { State: StepState.Running })
        {
            return (null, null, null, "Only a step that is running gets what it needs. Report the step as running first.");
        }

        DeploymentSnapshot snapshot = await database.DeploymentSnapshots
            .AsNoTracking()
            .SingleAsync(s => s.DeploymentId == runId, cancellationToken)
            .ConfigureAwait(false);

        return (run, SequenceDocuments.Read(snapshot.Definition).Steps.FirstOrDefault(s => s.Id == stepId), RunInputs.Read(inputs), null);
    }

    private void Audit(Deployment run, Machine machine, string? address, string detail) =>
        database.AuditEvents.Add(new AuditEvent
        {
            OccurredUtc = timeProvider.GetUtcNow(),
            Action = AuditActions.DeploymentSecretRead,
            ActorMachineId = machine.Id,
            SubjectId = run.Id.ToString("D"),
            SourceAddress = address,
            Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
        });
}

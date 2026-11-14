// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// The answer file a run needs. It's only handed to its agent while its step runs, and every read is audited. The
// caller saves the audit rows before it answers.
public sealed class RunAnswerFiles(
    DdtDbContext database,
    UnattendRenderer renderer,
    DdtSettings settings,
    RunQueries queries,
    FirstLogonSecrets firstLogon,
    TimeProvider timeProvider)
{
    public async Task<(string? AnswerFile, string? Refusal)> AnswerFileAsync(
        Machine machine,
        Guid runId,
        Guid stepId,
        string? address,
        CancellationToken cancellationToken)
    {
        (RunningStep? running, string? refusal) = await queries.RunningStepAsync(machine, runId, stepId, cancellationToken).ConfigureAwait(false);

        if (running is null)
        {
            return (null, refusal);
        }

        if (running.Step is not WriteUnattendStep unattend)
        {
            return (null, "That step writes no answer file.");
        }

        DeploymentOptions deployment = settings.Current.Deployment;
        string? password = deployment.LocalAdministrator.Password;

        if (unattend.LocalAdministrator && string.IsNullOrEmpty(password))
        {
            return (null, "The step adds the local administrator, but DDT:Deployment:LocalAdministrator has no password any more.");
        }

        (FirstLogon? signIn, string? signInRefusal) = await FirstLogonAsync(running, unattend, cancellationToken).ConfigureAwait(false);

        if (signInRefusal is not null)
        {
            return (null, signInRefusal);
        }

        (string? language, string? edition) = await AppliedImageAsync(runId, cancellationToken).ConfigureAwait(false);

        UnattendSources sources = new(running.Inputs)
        {
            ImageLanguage = language,
            ImageEdition = edition,
            Password = password,
            ProductKey = deployment.ProductKey,
            MacAddress = machine.PrimaryMac,
            FirstLogon = signIn,
        };
        (string? answerFile, string? problem) = renderer.Render(sources, unattend, RunSecrets.TemplateValues(machine, running.Run));

        if (problem is not null)
        {
            return (null, problem);
        }

        string with = signIn?.Account is { } account ? $", with {account} to sign in as" : "";
        database.AuditEvents.Add(RunSecrets.SecretRead(
            running.Run,
            machine,
            address,
            timeProvider.GetUtcNow(),
            $"The answer file of step {unattend.Name} ({stepId:D}) of {running.Run.Title}{with}."));

        return (answerFile, null);
    }

    // The image of the step that applied one, or of the first image step before any did. Of several branches, only one
    // ran. The edition comes from the library, which keeps an image while a run uses it.
    private async Task<(string? Language, string? Edition)> AppliedImageAsync(Guid runId, CancellationToken cancellationToken)
    {
        List<DeploymentArtifact> images = await database.DeploymentArtifacts
            .AsNoTracking()
            .Where(a => a.DeploymentId == runId && a.Kind == ArtifactKind.Image)
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        HashSet<Guid> steps = [.. images.Select(a => a.StepId)];
        List<Guid> done = await database.DeploymentSteps
            .AsNoTracking()
            .Where(step => step.DeploymentId == runId && step.State == StepState.Done && steps.Contains(step.StepId))
            .Select(step => step.StepId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if ((images.FirstOrDefault(a => done.Contains(a.StepId)) ?? images.FirstOrDefault()) is not { } applied)
        {
            return (null, null);
        }

        string? edition = await database.Images
            .AsNoTracking()
            .Where(image => image.Id == applied.SourceId)
            .Select(image => image.Edition)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return (applied.Language, edition);
    }

    // Only a sequence without a Windows step has them in its answer file.
    private async Task<(FirstLogon? FirstLogon, string? Refusal)> FirstLogonAsync(RunningStep running, WriteUnattendStep unattend, CancellationToken cancellationToken) =>
        unattend.SetsFirstLogon && FirstLogonSecrets.InAnswerFile(running.Definition)
            ? await firstLogon.ReadAsync(running.Run, running.Definition, running.Inputs, unattend, cancellationToken).ConfigureAwait(false)
            : (null, null);
}

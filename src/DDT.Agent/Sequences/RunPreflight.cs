// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Checks a fresh run before it changes anything, so nothing is erased for a run that cannot succeed. Each check
// throws DeploymentStepException with what to do about it.
internal sealed class RunPreflight(
    IAgentServer server,
    RunDiskChoice disks,
    IImageApplier applier,
    AgentLog log,
    TimeProvider timeProvider,
    string systemDirectory)
{
    public async Task CheckAsync(RunSession session, LocalDisk? confirmedDisk, CancellationToken cancellationToken)
    {
        AgentRun run = session.Run;
        CheckSequence(run);
        RunInventory inventory = InventoryOf(run);
        CheckSecureBoot(session, inventory.RawImages);
        CheckTools(inventory.Steps);

        if (inventory.Steps.Any(step => step.ErasesDisk))
        {
            session.Disk = await disks.ChooseAsync(run, confirmedDisk, inventory, cancellationToken).ConfigureAwait(false);
        }

        if (inventory.Images.Count > 0)
        {
            applier.Prepare();
        }

        await CheckFilesAsync(session, inventory, cancellationToken).ConfigureAwait(false);
        log.Information(session.Disk is { } chosen ? $"Running {run.SequenceName} on {chosen.Describe()}." : $"Running {run.SequenceName}.");
    }

    // A seed step that runs whatever happens needs every value it uses. The run has them once its inputs are answered,
    // before any step touches the disk. A seed step with conditions, inside a container, or allowed to fail is left for
    // the step itself to check.
    public static void CheckSeeds(AgentRun run, MachineVariables machine)
    {
        if (!SequenceTree.Nodes(run.Sequence).OfType<WriteRawImageStep>().Any())
        {
            return;
        }

        foreach (WriteCloudInitSeedStep seed in run.Sequence.Steps.OfType<WriteCloudInitSeedStep>()
            .Where(seed => seed.Conditions.Count == 0 && seed.When is null && !seed.ContinueOnError))
        {
            WriteCloudInitSeedStepRunner.Render(seed, machine.Value(MachineVariableNames.ComputerName), machine);
        }
    }

    // The server checked the sequence when it was saved. This checks it against what this agent can run.
    private static void CheckSequence(AgentRun run)
    {
        if (SequenceValidator.Validate(run.Sequence) is [var problem, ..])
        {
            throw new DeploymentStepException($"{run.SequenceName} cannot run: {problem.Message} Correct the sequence and assign it again.");
        }
    }

    private static RunInventory InventoryOf(AgentRun run)
    {
        IReadOnlyList<SequenceStep> steps = SequenceTree.Nodes(run.Sequence);
        List<AgentRunImage> images = [];

        foreach (ApplyImageStep step in steps.OfType<ApplyImageStep>())
        {
            images.Add(run.Images.FirstOrDefault(image => image.ImageId == step.ImageId)
                ?? throw new DeploymentStepException($"The server sent no image for step {step.Name}. Assign the sequence again."));
        }

        List<AgentRunImage> rawImages = [.. steps.OfType<WriteRawImageStep>().Select(step => WriteRawImageStepRunner.ImageOf(run, step))];

        return new RunInventory(steps, images, rawImages);
    }

    private void CheckSecureBoot(RunSession session, IReadOnlyList<AgentRunImage> rawImages)
    {
        bool allowed = session.Run.AllowSecureBootMismatch;

        foreach (AgentRunImage rawImage in rawImages.DistinctBy(image => image.Sha256))
        {
            if (SecureBootGate.Refusal(rawImage, allowed, session.SecureBootEnabled, session.TrustedUefiCas) is { } refusal)
            {
                throw new DeploymentStepException(refusal);
            }

            if (SecureBootGate.Warning(rawImage, allowed, session.SecureBootEnabled, session.TrustedUefiCas) is { } warning)
            {
                log.Warning(warning);
            }

            if (SecureBootGate.Unknown(rawImage, session.SecureBootEnabled, session.TrustedUefiCas) is { } unknown)
            {
                log.Warning(unknown);
            }
        }
    }

    // A lean boot image may lack DISM or PowerShell. The installed Windows brings its own PowerShell.
    private void CheckTools(IReadOnlyList<SequenceStep> steps)
    {
        if (steps.OfType<InjectDriversStep>().Any() && !File.Exists(InjectDriversStepRunner.DismIn(systemDirectory)))
        {
            throw new DeploymentStepException(InjectDriversStepRunner.NoDismMessage);
        }

        if (steps.OfType<RunScriptStep>().Any(step => step is { Interpreter: ScriptInterpreter.PowerShell, Phase: SequencePhase.WindowsPE })
            && !File.Exists(RunScriptStepRunner.PowerShellIn(systemDirectory)))
        {
            throw new DeploymentStepException(RunScriptStepRunner.NoPowerShellMessage);
        }
    }

    // The server's file for each image has to be the size the run expects, or the download would fail after the erase.
    private async Task CheckFilesAsync(RunSession session, RunInventory inventory, CancellationToken cancellationToken)
    {
        foreach (AgentRunImage image in inventory.Images.Concat(inventory.RawImages).DistinctBy(image => image.Sha256))
        {
            long? length = await ServerCallRules.CallAsync(
                call => server.HeadRunFileAsync(session.MachineId, session.Tokens.Token, session.Run.Id, image.Sha256, call),
                "the image check",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);

            if (length is null)
            {
                log.Warning($"The server did not say how large {image.Name} is. The download checks it instead.");
            }
            else if (length != image.SizeBytes)
            {
                throw new DeploymentStepException(
                    $"The server's file for {image.Name} holds {length} bytes instead of {image.SizeBytes}. Upload the image again.");
            }
        }
    }
}

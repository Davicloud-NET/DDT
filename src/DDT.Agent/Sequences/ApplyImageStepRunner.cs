// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Downloads the image onto the Windows volume, applies it there and deletes the download, which must not stay on the
// disk Windows starts from. The download is the first 40 percent of the step, the apply the rest.
public sealed class ApplyImageStepRunner(IImageApplier applier, RunDownloads downloads, RunSession session, AgentLog log)
{
    public const int DownloadPercent = 40;

    public async Task<StepResult> RunAsync(ApplyImageStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        AgentRunImage image = session.Run.Images.FirstOrDefault(candidate => candidate.ImageId == step.ImageId)
            ?? throw new DeploymentStepException("The server sent no image for this step. Assign the sequence again.");
        TargetVolumes volumes = session.RequireVolumes();
        string cache = Path.Combine(session.RunDirectory!, "cache");

        try
        {
            ScaledProgress downloading = new(context.Progress, 0, DownloadPercent);
            string wim = await downloads
                .DownloadAsync(image.Name, image.Sha256, image.SizeBytes, cache, ".wim", downloading, cancellationToken)
                .ConfigureAwait(false);

            log.Information($"Applying image {image.WimIndex} of {image.Name} to {volumes.Windows}.");
            ScaledProgress applying = new(context.Progress, DownloadPercent, 100);
            await applier.ApplyAsync(wim, image.WimIndex, volumes.Windows, applying, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Leftovers.Delete(cache, log);
        }

        return StepResult.Done(new Dictionary<string, string>(StringComparer.Ordinal) { [RunVariables.WindowsApplied] = RunVariables.Set });
    }
}

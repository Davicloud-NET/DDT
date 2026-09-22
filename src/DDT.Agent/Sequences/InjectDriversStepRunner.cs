// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Adds the driver packages the server matched to this machine's model to the applied Windows, one package at a time:
// download, unpack, then Windows PE's DISM on the offline image. DISM's scratch directory is on the target disk,
// because Windows PE's own scratch space holds only 512 MB.
public sealed class InjectDriversStepRunner(IToolRunner tools, RunDownloads downloads, RunSession session, AgentLog log)
{
    public static string DismPath => Path.Combine(Environment.SystemDirectory, "dism.exe");

    public async Task<StepResult> RunAsync(InjectDriversStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        AgentRunPackage[] packages = [.. session.Run.Packages.Where(package => package.StepId == step.Id)];
        string model = context.Machine.Model ?? "unknown";

        if (packages.Length == 0)
        {
            if (step.RequireMatch)
            {
                return StepResult.Failed($"The server has no driver package for this model ({model}), and this step requires one.");
            }

            log.Information($"The server has no driver package for this model ({model}), so no drivers are added.");

            return StepResult.Done();
        }

        TargetVolumes volumes = session.RequireVolumes();
        string directory = session.RunDirectory!;
        string cache = Path.Combine(directory, "cache");
        string unpacked = Path.Combine(directory, "packages", step.Id.ToString("D"));
        string scratch = Path.Combine(directory, "scratch");
        string logs = Path.Combine(directory, "logs");
        string dismLog = Path.Combine(logs, $"dism-{step.Id:D}.log");
        Directory.CreateDirectory(scratch);
        Directory.CreateDirectory(logs);

        try
        {
            for (int index = 0; index < packages.Length; index++)
            {
                AgentRunPackage package = packages[index];
                int start = index * 100 / packages.Length;
                int end = (index + 1) * 100 / packages.Length;
                string drivers = Path.Combine(unpacked, index.ToString(CultureInfo.InvariantCulture));

                await downloads
                    .UnpackAsync(package, cache, drivers, new ScaledProgress(context.Progress, start, (start + end) / 2), cancellationToken)
                    .ConfigureAwait(false);

                log.Information($"Adding the drivers of package {package.Name} to the applied Windows.");
                await tools.RunAsync(
                    DismPath,
                    [
                        $"/Image:{volumes.Windows}",
                        "/Add-Driver",
                        $"/Driver:{drivers}",
                        "/Recurse",
                        $"/ScratchDir:{scratch}",
                        $"/LogPath:{dismLog}",
                    ],
                    cancellationToken).ConfigureAwait(false);

                Leftovers.Delete(drivers, log);
                context.Progress.Report(end);
            }
        }
        finally
        {
            Leftovers.Delete(unpacked, log);
            Leftovers.Delete(scratch, log);
        }

        return StepResult.Done();
    }
}

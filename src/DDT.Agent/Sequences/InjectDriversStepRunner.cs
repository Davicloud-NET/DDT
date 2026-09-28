// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Adds the driver packages the server matched to this machine's model to the applied Windows with Windows PE's DISM.
public sealed class InjectDriversStepRunner(IToolRunner tools, RunDownloads downloads, RunSession session, AgentLog log)
    : IStepKindRunner<InjectDriversStep>
{
    public const string NoDismMessage =
        "This boot image has no DISM, which a driver step of this sequence needs. Build the boot image again with " +
        "build\\Build-BootImage.ps1.";

    public static string DismPath => DismIn(Environment.SystemDirectory);

    public static string DismIn(string systemDirectory) => Path.Combine(systemDirectory, "dism.exe");

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

        // On the target disk, because WinPE's scratch space only holds 512 MB.
        string scratch = Path.Combine(directory, "scratch");
        string logs = Path.Combine(directory, "logs");
        Directory.CreateDirectory(scratch);
        Directory.CreateDirectory(logs);
        DismRun dism = new(volumes.Windows, scratch, Path.Combine(logs, $"dism-{step.Id:D}.log"));

        try
        {
            for (int index = 0; index < packages.Length; index++)
            {
                int start = index * 100 / packages.Length;
                int end = (index + 1) * 100 / packages.Length;
                string drivers = Path.Combine(unpacked, index.ToString(CultureInfo.InvariantCulture));

                await downloads
                    .UnpackAsync(packages[index], cache, drivers, new ScaledProgress(context.Progress, start, (start + end) / 2), cancellationToken)
                    .ConfigureAwait(false);
                await AddAsync(packages[index], drivers, dism, cancellationToken).ConfigureAwait(false);
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

    private async Task AddAsync(AgentRunPackage package, string drivers, DismRun dism, CancellationToken cancellationToken)
    {
        log.Information($"Adding the drivers of package {package.Name} to the applied Windows.");
        await tools.RunAsync(
            DismPath,
            [
                $"/Image:{dism.Windows}",
                "/Add-Driver",
                $"/Driver:{drivers}",
                "/Recurse",
                $"/ScratchDir:{dism.Scratch}",
                $"/LogPath:{dism.Log}",
            ],
            cancellationToken).ConfigureAwait(false);

        Leftovers.Delete(drivers, log);
    }

    // The offline Windows DISM adds the drivers to, its scratch directory and its log.
    private sealed record DismRun(string Windows, string Scratch, string Log);
}

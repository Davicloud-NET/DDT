// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Servicing;

// Adds the step's Windows packages to the applied Windows, then cleans up its component store. A matched update that
// doesn't fit this Windows is left out; a listed package that doesn't fails the step. Scratch, packages and DISM's logs
// lie on the target disk, because WinPE's scratch space only holds 512 MB, and are deleted when the step ends.
public sealed class ServicePackagesStepRunner(IOfflineServicing dism, RunDownloads downloads, RunSession session, AgentLog log)
    : IStepKindRunner<ServicePackagesStep>
{
    // A 5.3 GB update took 13.2 GB of scratch at its peak, and kept 8 GB until the cleanup.
    public const double UpdateSpaceFactor = 3.5;

    private const int Unpacked = 20;
    private const int Added = 95;

    public static string DoesNotFit(string names) => $"{names} does not fit this Windows, so DISM did not add it.";

    public async Task<StepResult> RunAsync(ServicePackagesStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        AgentRunPackage[] packages = [.. session.Run.Packages.Where(package => package.StepId == step.Id)];

        if (packages.Length == 0)
        {
            log.Information("No Windows package is there for this image, so none is added.");

            return StepResult.Done();
        }

        TargetVolumes volumes = session.RequireVolumes();
        string directory = RunFiles.DirectoryIn(volumes.Windows);
        string unpacked = Path.Combine(directory, "packages", step.Id.ToString("D"));
        string scratch = Path.Combine(directory, "scratch");
        Directory.CreateDirectory(scratch);
        List<string> logs = [];

        try
        {
            IReadOnlyList<UnpackedPackage> folders = await UnpackAsync(packages, directory, unpacked, context, cancellationToken).ConfigureAwait(false);
            ServicingTally tally = new();

            if (await AddAllAsync(ServicingPlan.Of(folders), tally, () => Image(volumes, scratch, directory, logs), context, cancellationToken)
                .ConfigureAwait(false) is { } error)
            {
                return StepResult.Failed(error);
            }

            context.Progress.Report(Added);
            Version? after = step.Cleanup
                ? await CleanupAsync(Image(volumes, scratch, directory, logs), context, cancellationToken).ConfigureAwait(false)
                : tally.Before is null ? null : await dism.VersionAsync(Image(volumes, scratch, directory, logs), cancellationToken).ConfigureAwait(false);

            tally.Report(log, after);
            context.Progress.Report(100);

            return StepResult.Done();
        }
        finally
        {
            Leftovers.Delete(unpacked, log);
            Leftovers.Delete(scratch, log);

            foreach (string file in logs)
            {
                Leftovers.Delete(file, log);
            }
        }
    }

    private async Task<IReadOnlyList<UnpackedPackage>> UnpackAsync(
        AgentRunPackage[] packages,
        string directory,
        string unpacked,
        StepContext context,
        CancellationToken cancellationToken)
    {
        List<UnpackedPackage> folders = [];
        string cache = Path.Combine(directory, "cache");

        for (int index = 0; index < packages.Length; index++)
        {
            string folder = Path.Combine(unpacked, index.ToString(CultureInfo.InvariantCulture));
            IProgress<int> progress = new ScaledProgress(context.Progress, index * Unpacked / packages.Length, (index + 1) * Unpacked / packages.Length);

            await downloads.UnpackAsync(packages[index], cache, folder, progress, cancellationToken).ConfigureAwait(false);
            folders.Add(new UnpackedPackage(packages[index], folder));
        }

        return folders;
    }

    // The step's error, or null once each call added its files or left out a matched update. Each call gets its own log.
    private async Task<string?> AddAllAsync(
        IReadOnlyList<ServicingCall> calls,
        ServicingTally tally,
        Func<OfflineImage> image,
        StepContext context,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < calls.Count; index++)
        {
            IProgress<int> progress = new ScaledProgress(
                context.Progress,
                Unpacked + (index * (Added - Unpacked) / calls.Count),
                Unpacked + ((index + 1) * (Added - Unpacked) / calls.Count));

            context.Detail.Report(StepDetails.Coded(StepDetails.AddingPackage, calls[index].Names));

            if (await AddAsync(calls[index], tally, image(), progress, cancellationToken).ConfigureAwait(false) is { } error)
            {
                return error;
            }
        }

        return null;
    }

    // An error for the step, or null once DISM added the call's files or left out a matched update.
    private async Task<string?> AddAsync(
        ServicingCall call,
        ServicingTally tally,
        OfflineImage image,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        if (call.IsUpdate && SpaceProblem(call, image) is { } space)
        {
            return space;
        }

        log.Information($"Adding {call.Names}: {string.Join(", ", call.Files.Select(Path.GetFileName))}.");
        ServicingAnswer answer = await dism.AddAsync(image, call.Files, progress, cancellationToken).ConfigureAwait(false);

        if (answer.Outcome == ServicingOutcome.NotApplicable)
        {
            if (!call.Packages.All(package => package.Package.Matched))
            {
                return DoesNotFit(call.Names);
            }

            log.Warning($"{DoesNotFit(call.Names)} It was matched by its build, so it is left out.");
        }

        tally.Count(call, answer);

        // An update's files are many gigabytes, which the next update may need.
        if (call.IsUpdate)
        {
            Leftovers.Delete(call.Packages[0].Folder, log);
        }

        return null;
    }

    private string? SpaceProblem(ServicingCall call, OfflineImage image)
    {
        long size = Directory.EnumerateFiles(call.Packages[0].Folder).Sum(file => new FileInfo(file).Length);
        long needed = (long)(size * UpdateSpaceFactor);
        long free = downloads.AvailableBytes(image.Windows);

        if (free >= needed)
        {
            log.Information($"The Windows partition has {ByteSize.Format(free)} free, and adding {call.Names} needs {ByteSize.Format(needed)}.");

            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Adding {call.Names} needs {ByteSize.Format(needed)} free on the Windows partition, {UpdateSpaceFactor} times its {ByteSize.Format(size)}, but it has {ByteSize.Format(free)}.");
    }

    // The packages are in by now, so a failed cleanup only leaves the image larger. Returns the image's version.
    private async Task<Version?> CleanupAsync(OfflineImage image, StepContext context, CancellationToken cancellationToken)
    {
        context.Detail.Report(StepDetails.Coded(StepDetails.CleaningUp));
        log.Information("Cleaning up the image's component store.");

        try
        {
            return await dism.CleanupAsync(image, cancellationToken).ConfigureAwait(false);
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"{exception.Message} The packages are added; the image keeps the components they replaced.");

            return null;
        }
    }

    private static OfflineImage Image(TargetVolumes volumes, string scratch, string directory, List<string> logs)
    {
        string file = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"dism-{logs.Count + 1}.log"));
        logs.Add(file);

        return new OfflineImage(volumes.Windows, scratch, file);
    }
}

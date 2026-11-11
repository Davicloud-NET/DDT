// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent.Applications;

// Where installers run: an application's files unpacked under packages\<step>\<application> in the run's directory, or
// the run's scripts folder for one without files, as a script without a package runs.
public sealed class ApplicationFolders(RunDownloads downloads, RunSession session, string workDirectory)
{
    // Installers write beside their files, into the cache and into Program Files, so the check keeps room for that.
    public const long SpareBytes = 1024L * 1024 * 1024;

    private string RunDirectory => session.RunDirectory ?? workDirectory;

    public string Scripts()
    {
        string scripts = Path.Combine(RunDirectory, StepFiles.ScriptsFolder);
        Directory.CreateDirectory(scripts);

        return scripts;
    }

    // Where the application's files go, or null without files.
    public string? PackageOf(ApplicationInstall install)
    {
        ArgumentNullException.ThrowIfNull(install);

        return install.Application.Files is null
            ? null
            : Path.Combine(RunDirectory, StepFiles.PackagesFolder, install.StepId.ToString("D"), install.Application.Id.ToString("D"));
    }

    // Unpacks the files into PackageOf. Throws DeploymentStepException when the disk has too little room.
    public async Task UnpackAsync(ApplicationInstall install, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(install);

        if (install.Application.Files is not { } files || PackageOf(install) is not { } target)
        {
            return;
        }

        string directory = RunDirectory;
        long needed = files.SizeBytes + files.ExpandedBytes + SpareBytes;
        long available = downloads.AvailableBytes(directory);

        if (available < needed)
        {
            throw new DeploymentStepException(
                $"Its files need {ByteSize.Format(needed)} free to download and unpack, {ByteSize.Format(SpareBytes)} to spare included, " +
                $"and the disk has {ByteSize.Format(available)} free.");
        }

        AgentRunPackage package = new(install.StepId, files.Name, files.Sha256, files.SizeBytes);
        await downloads.UnpackAsync(package, Path.Combine(directory, StepFiles.CacheFolder), target, percent, cancellationToken).ConfigureAwait(false);
    }
}

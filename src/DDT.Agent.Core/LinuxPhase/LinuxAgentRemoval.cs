// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.InstalledPhase;
using DDT.Agent.Sequences;
using DDT.Core.CloudInit;

namespace DDT.Agent.LinuxPhase;

// Removes the agent from the installed Linux once its run is over there. The run token goes first. The unit goes last
// but for the program, so systemd finds nothing missing while it still has the unit. Nothing here fails: what cannot be
// removed stays and is said. The agent then has to end with exit code 0, or systemd keeps a failed unit without a file.
public sealed class LinuxAgentRemoval(LinuxAgentPaths paths, IMountedSeeds seeds, IToolRunner tools, AgentLog log) : IAgentRemoval
{
    // What the agent keeps beside the run's own files. A folder is only deleted when nothing else is left in it,
    // because a DDT server on the same system keeps its store under the same name.
    private static readonly string[] s_ownFolders = ["scripts", "packages", "cache"];

    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        log.Information("The run is over here, so the agent removes itself.");

        RunFiles.In(paths.State, log).Discard();
        RemoveFromSeeds();
        RemoveCloudInitCopy();
        RemoveState();
        await SystemctlAsync(["disable", LinuxAgentFiles.UnitName], cancellationToken).ConfigureAwait(false);
        Leftovers.Delete(paths.Unit, log);
        await SystemctlAsync(["daemon-reload"], cancellationToken).ConfigureAwait(false);
        Leftovers.Delete(paths.Agent, log);
        DeleteEmpty(paths.Program);
        RemoveFromRunFolder();

        log.Information("The agent is removed from this Linux, but for what a line above says stays.");
    }

    // The agent, its settings and the run's state as the hand-over put them there, and the script cloud-init ran.
    private void RemoveFromSeeds()
    {
        int mounted = seeds.Each(seed =>
        {
            Leftovers.WipeAll(Path.Combine(seed, LinuxAgentFiles.SeedFolder), log);
            DeleteScript(Path.Combine(seed, CloudInitSeed.VendorData));
        });

        if (mounted == 0)
        {
            log.Warning(
                $"No cloud-init seed could be mounted, so the folder {LinuxAgentFiles.SeedFolder} and {CloudInitSeed.VendorData} stay on " +
                "it. They hold the agent and its script.");
        }
    }

    // cloud-init copies each script of the vendor-data into its instance folder before it runs it.
    private void RemoveCloudInitCopy()
    {
        try
        {
            if (Directory.Exists(paths.VendorScripts))
            {
                foreach (string script in Directory.GetFiles(paths.VendorScripts))
                {
                    DeleteScript(script);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"{paths.VendorScripts} could not be read ({exception.Message}), so cloud-init's copy of the agent's script stays.");
        }
    }

    // Only the agent's own script. An administrator's vendor-data is none of DDT's.
    private void DeleteScript(string file)
    {
        try
        {
            if (File.Exists(file) && new FileInfo(file).Length <= 64 * 1024 && VendorDataScript.IsOurs(File.ReadAllBytes(file)))
            {
                Leftovers.Delete(file, log);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"{file} could not be read ({exception.Message}), so it stays.");
        }
    }

    // A script's text and what a script left behind may hold what only root should read, so files are overwritten.
    private void RemoveState()
    {
        string run = RunFiles.DirectoryIn(paths.State);

        foreach (string folder in s_ownFolders)
        {
            Leftovers.WipeAll(Path.Combine(run, folder), log);
        }

        if (run != paths.State)
        {
            DeleteEmpty(run);
        }

        Leftovers.Delete(Path.GetDirectoryName(paths.LogFile) ?? paths.LogFile, log);
        Leftovers.Delete(paths.Configuration, log);
        DeleteEmpty(paths.State);
    }

    // /run is empty again after the next start, so what stays here only stays until then.
    private void RemoveFromRunFolder()
    {
        Leftovers.Delete(Path.Combine(paths.Run, LinuxRestartMarker.FileName), log);

        foreach (string folder in (string[])[paths.Shares, paths.SeedMount, paths.Run])
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Leftovers.Delete(folder, log);
            }
        }
    }

    private void DeleteEmpty(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            if (Directory.EnumerateFileSystemEntries(folder).Any())
            {
                log.Warning($"{folder} holds what the agent did not put there, so the folder stays.");

                return;
            }

            Directory.Delete(folder, recursive: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"{folder} could not be deleted ({exception.Message}).");
        }
    }

    private async Task SystemctlAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            await tools.RunAsync(LinuxPhaseRebooter.Systemctl, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"{ToolRunner.CommandLine(LinuxPhaseRebooter.Systemctl, arguments)} did not work ({exception.Message}). Run it by hand.");
        }
    }
}

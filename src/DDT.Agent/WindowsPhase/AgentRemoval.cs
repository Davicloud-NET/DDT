// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;

namespace DDT.Agent.WindowsPhase;

// Takes the agent off the Windows at windowsRoot once its run is over: the run's token first, so nothing left can act
// as the machine, then its state, so nothing can go on with the run, then what the steps left and the logs, whose
// lines the server has. The service is only marked for deletion while it runs, and goes when its process ends. The
// agent, its agent.json, the log it still writes, whatever else is still in use and the directories go when Windows
// next starts, after which nothing of DDT is left. Whatever cannot be deleted only stays behind, so nothing here fails.
public sealed class AgentRemoval(string windowsRoot, IToolRunner tools, IRestartDeleter deleter, AgentLog log) : IAgentRemoval
{
    public static string ScPath => Path.Combine(Environment.SystemDirectory, "sc.exe");

    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        log.Information("The run is over here, so the agent removes itself.");

        string directory = Path.Combine(windowsRoot, "DDT");
        string agent = Path.Combine(directory, WindowsHandOver.AgentDirectory);
        string ownLog = WindowsPhaseService.LogPathIn(windowsRoot);
        string logs = Path.GetDirectoryName(ownLog)!;

        RunFiles.In(windowsRoot, log).Discard();
        DeleteAllBut(directory, agent, logs);
        DeleteAllBut(logs, ownLog);

        try
        {
            await tools.RunAsync(ScPath, ["delete", OfflineServiceRegistration.ServiceName], cancellationToken).ConfigureAwait(false);
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"The service {OfflineServiceRegistration.ServiceName} could not be deleted ({exception.Message}). Delete it with sc.exe delete {OfflineServiceRegistration.ServiceName}.");
        }

        // The agent, its agent.json and the log it writes, and whatever a process that a step started still holds, such
        // as a script's working directory or a package's program.
        if (Directory.Exists(directory))
        {
            MarkAtRestart(new DirectoryInfo(directory));
        }
    }

    // What the directory holds before the directory itself, as Windows deletes a directory only once it is empty. A
    // link is marked itself and never followed, so nothing outside is ever marked.
    private void MarkAtRestart(DirectoryInfo directory)
    {
        FileSystemInfo[] entries;

        try
        {
            entries = directory.GetFileSystemInfos();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"{directory.FullName} could not be read ({exception.Message}), so what it holds stays.");
            entries = [];
        }

        foreach (FileSystemInfo entry in entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (entry is DirectoryInfo inner && !inner.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                MarkAtRestart(inner);
            }
            else
            {
                deleter.DeleteAtRestart(entry.FullName);
            }
        }

        deleter.DeleteAtRestart(directory.FullName);
    }

    private void DeleteAllBut(string directory, params string[] kept)
    {
        string[] entries;

        try
        {
            entries = Directory.Exists(directory) ? Directory.GetFileSystemEntries(directory) : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"{directory} could not be read ({exception.Message}), so what it holds stays.");

            return;
        }

        foreach (string entry in entries.Where(entry => !kept.Contains(entry, StringComparer.OrdinalIgnoreCase)))
        {
            Leftovers.Delete(entry, log);
        }
    }
}

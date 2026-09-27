// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.AccessControl;
using System.Text.Json;
using DDT.Agent.WindowsPhase;
using DDT.ConsoleProtocol;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// Prepares the installed Windows to go on with the run: this agent, as agentPath names it, goes into the run's
// directory as <Windows volume>\DDT\agent\ddt-agent.exe, with configuration as its agent.json, which holds only the
// server's URL and root certificate; the service that starts the agent there is registered; and the run's state is
// saved as in the Windows phase, with the run token. The agent's own file, not the boot image's: the running one may
// be a newer one it updated itself to. The graphical console, when consoleDirectory holds one, goes into
// <Windows volume>\DDT\console, where DDT's session in Windows starts it as its shell.
public sealed class WindowsHandOver(
    OfflineServiceRegistration service,
    string agentPath,
    AgentConfiguration configuration,
    AgentLog log,
    bool dryRun,
    string? consoleDirectory = null)
{
    public const string AgentDirectory = "agent";
    public const string AgentFileName = "ddt-agent.exe";
    public const string ConfigurationFileName = "agent.json";
    public const string ConsoleDirectory = "console";

    // SYSTEM may change the console, and the session's account, a member of Users, only read and start it.
    public const string ConsoleSddl = "D:P(A;OICI;FA;;;SY)(A;OICI;0x1200a9;;;BU)";

    public async Task StageAsync(RunSession session, FileRunStateStore store, SequenceState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(state);

        string windows = session.RequireVolumes().Windows;

        // Inside the run's directory, whose DACL it inherits: only SYSTEM can change what the service starts.
        string directory = Path.Combine(session.RunDirectory!, AgentDirectory);
        Directory.CreateDirectory(directory);

        if (dryRun)
        {
            log.Information($"Dry run: {directory} is left open. In Windows PE it inherits the run directory's DACL ({SystemOnlyDirectory.Sddl}).");
        }

        File.Copy(agentPath, Path.Combine(directory, AgentFileName), overwrite: true);
        await File.WriteAllBytesAsync(
            Path.Combine(directory, ConfigurationFileName),
            JsonSerializer.SerializeToUtf8Bytes(configuration, AgentConfigurationJsonContext.Default.AgentConfiguration),
            cancellationToken).ConfigureAwait(false);
        log.Information($"The agent is in {directory}, to go on with the run in Windows.");

        if (StageConsole(session.RunDirectory!))
        {
            await DeploySession.PlanAsync(windows, log, cancellationToken).ConfigureAwait(false);
        }

        await service.RegisterAsync(windows, cancellationToken).ConfigureAwait(false);

        // Only once the service that goes on with the run exists. Like the engine's saves, without the stop token: the
        // state must match what the disk holds.
        await store.SaveAsync(state with { Phase = SequencePhase.Windows }, CancellationToken.None).ConfigureAwait(false);
    }

    // Without all of its files there is no console, and the run in Windows shows only on the server. True once staged.
    private bool StageConsole(string runDirectory)
    {
        if (consoleDirectory is null)
        {
            return false;
        }

        string[] missing = [.. ConsolePipe.Files.Where(file => !File.Exists(Path.Combine(consoleDirectory, file)))];

        if (missing.Length > 0)
        {
            log.Warning($"{consoleDirectory} lacks {string.Join(", ", missing)}, so the console does not come into Windows.");

            return false;
        }

        string directory = Path.Combine(runDirectory, ConsoleDirectory);

        if (dryRun)
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            DirectorySecurity security = new();
            security.SetSecurityDescriptorSddlForm(ConsoleSddl);
            new DirectoryInfo(directory).Create(security);
        }

        foreach (string file in ConsolePipe.Files)
        {
            File.Copy(Path.Combine(consoleDirectory, file), Path.Combine(directory, file), overwrite: true);
        }

        log.Information($"The console is in {directory}, to show the run in Windows.");

        return true;
    }
}

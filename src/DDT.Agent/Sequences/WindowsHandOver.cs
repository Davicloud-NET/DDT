// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.AccessControl;
using System.Text.Json;
using DDT.Agent.WindowsPhase;
using DDT.ConsoleProtocol;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// Prepares the installed Windows to continue the run. It stages this agent with its agent.json and the console for
// DDT's session, registers the service that starts the agent, and saves the run's state in the Windows phase.
public sealed class WindowsHandOver(
    OfflineServiceRegistration service,
    string agentPath,
    AgentConfiguration configuration,
    AgentLog log,
    bool dryRun,
    Func<string?>? consoleDirectory = null)
{
    public const string AgentDirectory = "agent";
    public const string AgentFileName = "ddt-agent.exe";
    public const string ConfigurationFileName = "agent.json";
    public const string ConsoleDirectory = "console";

    // SYSTEM may change the console. The session's account, a member of Users, may only read and start it.
    public const string ConsoleSddl = "D:P(A;OICI;FA;;;SY)(A;OICI;0x1200a9;;;BU)";

    public async Task StageAsync(RunSession session, FileRunStateStore store, SequenceState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(state);

        string windows = session.RequireVolumes().Windows;

        // Inside the run's directory, whose DACL it inherits, so only SYSTEM can change what the service starts.
        string directory = Path.Combine(session.RunDirectory!, AgentDirectory);
        Directory.CreateDirectory(directory);

        if (dryRun)
        {
            log.Information($"Dry run: {directory} is left open. In Windows PE it inherits the run directory's DACL ({SystemOnlyDirectory.Sddl}).");
        }

        // The running agent, not the boot image's, because it may have updated itself to a newer one.
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

        // Only once the service that continues the run exists. Like the engine's saves, this runs without the stop
        // token, because the state must match what the disk holds.
        await store.SaveAsync(state with { Phase = SequencePhase.Windows }, CancellationToken.None).ConfigureAwait(false);
    }

    // consoleDirectory is only asked now, once it's known whether the console that ran speaks this agent's protocol.
    // Without all of its files there's no console, and the run in Windows only shows on the server. True once staged.
    private bool StageConsole(string runDirectory)
    {
        if (consoleDirectory?.Invoke() is not { } source)
        {
            return false;
        }

        string[] missing = [.. ConsolePipe.Files.Where(file => !File.Exists(Path.Combine(source, file)))];

        if (missing.Length > 0)
        {
            log.Warning($"{source} lacks {string.Join(", ", missing)}, so the console does not come into Windows.");

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
            File.Copy(Path.Combine(source, file), Path.Combine(directory, file), overwrite: true);
        }

        log.Information($"The console is in {directory}, to show the run in Windows.");

        return true;
    }
}

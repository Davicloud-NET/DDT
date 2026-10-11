// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Text;
using DDT.Agent.Deployment;
using DDT.Agent.Facts;
using DDT.Agent.InstalledPhase;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent.LinuxPhase;

// What ddt-agent.service runs: the agent the vendor-data script installed, which goes on with the run and then removes
// itself. Nobody watches it, so it logs to the journal and to a file beside its state. It takes no newer agent.
public static class LinuxPhaseService
{
    // For an error nobody expected. systemd starts the unit again after it.
    public const int Crashed = 1;

    public static string NoSettings(string configuration) =>
        $"{configuration} is not there, so this system has no run for the DDT agent to go on with.";

    // systemd stops the unit with SIGTERM, also on the machine's way down for a restart. The run then ends cleanly
    // at the step it is in, and goes on when the unit starts again.
    public static async Task<int> RunAsync()
    {
        using CancellationTokenSource stop = new();
        using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, signal => Stop(signal, stop));
        using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, signal => Stop(signal, stop));

        return await RunAsync(LinuxAgentPaths.Machine, Console.Out, Environment.ProcessPath, stop.Token).ConfigureAwait(false);
    }

    // After the removal any exit code but 0 would leave a failed unit that has no file, so a finished run and a
    // restart end the unit like a stop.
    internal static async Task<int> RunAsync(LinuxAgentPaths paths, TextWriter output, string? processPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.Configuration))
        {
            return await EndWithoutSettingsAsync(paths, output, processPath).ConfigureAwait(false);
        }

        using JournalAndFile writer = new(output, LogFile(paths));
        AgentLog log = new(TimeProvider.System, writer, datedLines: true);

        try
        {
            int exitCode = await GoOnAsync(paths, log, cancellationToken).ConfigureAwait(false);

            return exitCode is AgentExitCodes.Deployed or AgentExitCodes.Restarting ? AgentExitCodes.Stopped : exitCode;
        }
        catch (Exception exception)
        {
            // The unit's boundary: the journal says what happened, and systemd starts the agent again
            log.Error($"The agent stopped on an unexpected error: {exception}");

            return Crashed;
        }
    }

    private static async Task<int> GoOnAsync(LinuxAgentPaths paths, AgentLog log, CancellationToken cancellationToken)
    {
        // Before anything that takes time: a restart the user-data asked for may stop the agent within a second.
        CloudInitFiles cloudInit = new(paths.Root);
        new FirstStartNote(cloudInit, paths.FirstStart, log).Take();

        LibcMounts mounts = new();
        LinuxSeeds seeds = new(mounts, paths, log);

        // Before the server: a token left on the seed is one more copy of what acts as this machine.
        new SeedTokenRepair(seeds, log).Run();

        if (!AgentOptions.TryParse(["--config", paths.Configuration], AgentEnvironment.LinuxBoot, out AgentOptions? options, out string error) || options is null)
        {
            log.Error($"{error} Without its settings the agent cannot reach the server, so it ends until Linux starts again.");

            return AgentExitCodes.Stopped;
        }

        using HttpAgentServer server = new(options.ServerUrl, options.RootCertificate)
        {
            Environment = AgentEnvironment.Linux,
            Lookup = new ServerLookup { Names = new NameLookup(), Known = options.ServerAddress, Log = log },
        };
        LinuxShareConnector shares = new(mounts, paths.Shares, log) { Kernel = KernelRelease };
        shares.DisconnectLeftovers();
        LinuxUefiVariables firmware = new(paths.Root);

        InstalledPhaseLoop loop = new LinuxPhaseLoopBuilder
        {
            Server = server,
            IdentityReader = new HardwareMachineIdentityReader(new LinuxFirmwareTables(paths.Root), new LinuxSystemHardware(paths.Root), firmware, new LinuxSecureBoot(firmware)),
            Paths = paths,
            Tools = Tools(paths, log),
            Scripts = new ShellScriptLauncher { Environment = LinuxShell.Environment(Environment.GetEnvironmentVariable) },
            Shares = shares,
            Seeds = seeds,
            CloudInit = cloudInit,
            RestartMarker = new LinuxRestartMarker(paths.Run, log),
            FreeSpace = new LinuxFreeSpace(),
            Log = log,
            TimeProvider = TimeProvider.System,
            AgentVersion = typeof(AgentLoop).Assembly.GetName().Version?.ToString(3) ?? "unknown",
        }.Build();

        return await loop.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    // A removal that was cut off leaves the unit and this program without their settings. The agent the unit starts
    // finishes it. An agent started from elsewhere touches nothing.
    private static async Task<int> EndWithoutSettingsAsync(LinuxAgentPaths paths, TextWriter output, string? processPath)
    {
        AgentLog log = new(TimeProvider.System, output, datedLines: true);
        log.Warning(NoSettings(paths.Configuration));

        if (string.Equals(processPath, paths.Agent, StringComparison.Ordinal))
        {
            LinuxSeeds seeds = new(new LibcMounts(), paths, log);
            await new LinuxAgentRemoval(paths, seeds, Tools(paths, log), log).RemoveAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return AgentExitCodes.Stopped;
    }

    // What a script left running goes when its step is stopped, as in the boot environment. The unit's file mode mask
    // is the agent's alone.
    private static ToolRunner Tools(LinuxAgentPaths paths, AgentLog log) => new(log, TimeProvider.System, Latin1Output.Decode)
    {
        Strays = new LinuxStrayProcesses(paths.Root, log, Environment.ProcessId) { Adopts = LinuxStrayProcesses.Adopt() },
        FileModeMask = OperatingSystem.IsLinux() ? FileModeMask.Set : null,
    };

    // Null when the file cannot be opened, as on a disk that is full. The journal still gets every line. The file is
    // root's alone, because a script's output may hold what only root should read.
    private static StreamWriter? LogFile(LinuxAgentPaths paths)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.LogFile) ?? paths.State);
            FileStreamOptions options = OwnFile.Private(FileMode.Append);
            options.Share = FileShare.ReadWrite | FileShare.Delete;
            FileStream file = new(paths.LogFile, options);

            return new StreamWriter(file, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string KernelRelease()
    {
        try
        {
            return File.ReadAllText("/proc/sys/kernel/osrelease").Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Environment.OSVersion.Version.ToString();
        }
    }

    private static void Stop(PosixSignalContext signal, CancellationTokenSource stop)
    {
        signal.Cancel = true;
        stop.Cancel();
    }
}

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

    // What systemd sets for a process whose output it connected to the journal.
    private const string JournalStream = "JOURNAL_STREAM";

    public static string NoSettings(string configuration) =>
        $"{configuration} is not there, so this system has no run for the DDT agent to go on with.";

    // systemd stops the unit with SIGTERM, also on the machine's way down for a restart. The run then ends cleanly
    // at the step it is in. The unit's KillMode is mixed, so the signal reaches the agent alone, and the agent asks
    // its script to end. A SIGTERM from anyone else is the same to it.
    public static async Task<int> RunAsync()
    {
        using CancellationTokenSource stop = new();
        using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, signal => Stop(signal, stop));
        using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, signal => Stop(signal, stop));
        LinuxPhaseMachine machine = LinuxPhaseMachine.Own() with { OutputIsJournal = Environment.GetEnvironmentVariable(JournalStream) is { Length: > 0 } };

        return await RunAsync(machine, stop.Token).ConfigureAwait(false);
    }

    // After the removal any exit code but 0 would leave a failed unit that has no file, so a finished run and a
    // restart end the unit like a stop.
    internal static async Task<int> RunAsync(LinuxPhaseMachine machine, CancellationToken cancellationToken)
    {
        bool hasSettings = File.Exists(machine.Paths.Configuration);

        // Without its settings the agent may be on a system that is not its own, where it makes no file either.
        using JournalAndFile writer = new(machine.Output, hasSettings ? LogFile(machine.Paths) : null, machine.OutputIsJournal);
        AgentLog log = new(TimeProvider.System, writer, datedLines: true);

        try
        {
            int exitCode = hasSettings
                ? await GoOnAsync(machine, log, cancellationToken).ConfigureAwait(false)
                : await EndWithoutSettingsAsync(machine, log).ConfigureAwait(false);

            return exitCode is AgentExitCodes.Deployed or AgentExitCodes.Restarting ? AgentExitCodes.Stopped : exitCode;
        }
        catch (Exception exception)
        {
            // The unit's boundary: the journal says what happened, and systemd starts the agent again
            log.Error($"The agent stopped on an unexpected error: {exception}");

            return Crashed;
        }
    }

    private static async Task<int> GoOnAsync(LinuxPhaseMachine machine, AgentLog log, CancellationToken cancellationToken)
    {
        LinuxAgentPaths paths = machine.Paths;

        // Before anything that takes time: a restart the user-data asked for may stop the agent within a second.
        CloudInitFiles cloudInit = new(paths.Root);
        new FirstStartNote(cloudInit, paths.FirstStart, log).Take();

        IMounts mounts = machine.Mounts ?? new LibcMounts();
        LinuxSeeds seeds = new(mounts, paths, log);

        // Before the server: a token left on the seed is one more copy of what acts as this machine.
        new SeedTokenRepair(seeds, log).Run();

        if (!AgentOptions.TryParse(["--config", paths.Configuration], AgentEnvironment.LinuxBoot, out AgentOptions? options, out string error) || options is null)
        {
            log.Error($"{error} Without its settings the agent cannot reach the server, so it ends until Linux starts again.");

            return AgentExitCodes.Stopped;
        }

        LinuxShareConnector shares = new(mounts, paths.Shares, log) { Kernel = KernelRelease };
        shares.DisconnectLeftovers();
        IAgentServer server = machine.Connect?.Invoke(options) ?? Server(options, log);

        try
        {
            InstalledPhaseLoop loop = new LinuxPhaseLoopBuilder
            {
                Server = server,
                IdentityReader = machine.Identity ?? Identity(paths),
                Paths = paths,
                Tools = Tools(machine, log),
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
        finally
        {
            (server as IDisposable)?.Dispose();
        }
    }

    // A removal that was cut off leaves the unit and this program without their settings. The agent the unit starts
    // finishes it. An agent started from elsewhere touches nothing. The program is known by where it really is:
    // /usr/local may be a link.
    private static async Task<int> EndWithoutSettingsAsync(LinuxPhaseMachine machine, AgentLog log)
    {
        LinuxAgentPaths paths = machine.Paths;
        log.Warning(NoSettings(paths.Configuration));

        if (RealPath.Same(machine.ProcessPath, paths.Agent))
        {
            LinuxSeeds seeds = new(machine.Mounts ?? new LibcMounts(), paths, log);
            await new LinuxAgentRemoval(paths, seeds, Tools(machine, log), log).RemoveAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return AgentExitCodes.Stopped;
    }

    // The name is looked up as an installed Linux needs it, with the address the boot environment used to fall back on.
    private static HttpAgentServer Server(AgentOptions options, AgentLog log) => new(options.ServerUrl, options.RootCertificate)
    {
        Environment = AgentEnvironment.Linux,
        Lookup = new ServerLookup { Names = new NameLookup(), Known = options.ServerAddress, Log = log },
    };

    private static HardwareMachineIdentityReader Identity(LinuxAgentPaths paths)
    {
        LinuxUefiVariables firmware = new(paths.Root);

        return new HardwareMachineIdentityReader(new LinuxFirmwareTables(paths.Root), new LinuxSystemHardware(paths.Root), firmware, new LinuxSecureBoot(firmware));
    }

    // What a script left running goes when its step is stopped, as in the boot environment. The unit's file mode mask
    // is the agent's alone, and systemctl says on its error output what it did.
    private static IToolRunner Tools(LinuxPhaseMachine machine, AgentLog log)
    {
        bool onLinux = OperatingSystem.IsLinux();
        ToolRunner tools = new(log, TimeProvider.System, Latin1Output.Decode)
        {
            Strays = new LinuxStrayProcesses(machine.Paths.Root, log, Environment.ProcessId) { Adopts = onLinux && LinuxStrayProcesses.Adopt() },
            FileModeMask = onLinux ? FileModeMask.Set : null,
            PlainErrorOutput = new HashSet<string>(StringComparer.Ordinal) { LinuxPhaseRebooter.Systemctl },
        };

        return machine.Tools?.Invoke(tools) ?? tools;
    }

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

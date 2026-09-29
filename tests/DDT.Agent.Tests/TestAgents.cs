// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;

namespace DDT.Agent.Tests;

internal static class TestAgents
{
    public const string Version = "1.0.0-test";

    public static AgentConfiguration Configuration { get; } =
        new("https://ddt.example:8443/", "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n", null);

    // Everything on the disk is under tools.Root: the volumes, WinPE's own directory X, and System32 with WinPE's
    // tools. Like in a dry run, unless options say otherwise, the run's directory stays open to the account the tests
    // run as. The restart marker is a real file, though.
    public static SequenceRunner Runner(
        IAgentServer server,
        FakeDeploymentTools tools,
        AgentLog log,
        TimeProvider timeProvider,
        TestRunnerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tools);

        options ??= new TestRunnerOptions();
        IToolRunner toolRunner = options.ToolRunner ?? new RecordingToolRunner();

        return new SequenceRunnerBuilder
        {
            Server = server,
            Partitioner = tools,
            RawDisks = options.RawDisks ?? new MemoryRawDisks(),
            Applier = tools,
            BcdWriter = options.BcdWriter ?? tools,
            Rebooter = options.Rebooter ?? tools,
            RestartMarker = RestartMarker(tools, log),
            Tools = toolRunner,
            Joiner = options.Joiner ?? tools,
            HandOver = HandOver(tools, toolRunner, log, options.DryRunHandOver, options.ConsoleDirectory),
            Log = log,
            TimeProvider = timeProvider,
            Options = new SequenceRunnerOptions(
                options.HeartbeatInterval ?? Timeout.InfiniteTimeSpan,
                Path.Combine(tools.Root, "X"),
                options.SystemDirectory ?? SystemDirectory(tools),
                options.DryRun),
            Status = options.Status,
        }.Build();
    }

    // The text console, asking through prompt.
    public static ConsoleStatus Status(ISignInPrompt prompt, AgentLog log) => Status(new TextMachineConsole(prompt, log));

    public static ConsoleStatus Status(IMachineConsole console) =>
        new(console, Version, new Uri(Configuration.ServerUrl!), "German (Germany)", dryRun: true);

    // WinPE keeps it in its own directory. Here it gets a separate one, because the tests' dry runs delete X when they
    // end and would delete the marker with it.
    public static WindowsPERestartMarker RestartMarker(FakeDeploymentTools tools, AgentLog log)
    {
        ArgumentNullException.ThrowIfNull(tools);

        return new WindowsPERestartMarker(Path.Combine(tools.Root, "RAM disk"), log, dryRun: false);
    }

    // Stages AgentSource(tools) with Configuration, and the console in consoleDirectory.
    public static WindowsHandOver HandOver(
        FakeDeploymentTools tools,
        IToolRunner toolRunner,
        AgentLog log,
        bool dryRun = false,
        string? consoleDirectory = null) =>
        new(
            new OfflineServiceRegistration(toolRunner, log, dryRun),
            AgentSource(tools),
            Configuration,
            log,
            dryRun,
            consoleDirectory is null ? null : () => consoleDirectory);

    // The running agent, under the name the self-update may have given it.
    public static string AgentSource(FakeDeploymentTools tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        string agent = Path.Combine(tools.Root, "Agent", "ddt-agent-5a5a.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(agent)!);
        File.WriteAllBytes(agent, [0x4D, 0x5A, 0x90, 0x00]);

        return agent;
    }

    // Holds the tools a sequence may need that Windows PE does not always have.
    public static string SystemDirectory(FakeDeploymentTools tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        string system = Path.Combine(tools.Root, "System32");
        string powerShell = RunScriptStepRunner.PowerShellIn(system);
        Directory.CreateDirectory(Path.GetDirectoryName(powerShell)!);
        File.WriteAllText(powerShell, string.Empty);
        File.WriteAllText(InjectDriversStepRunner.DismIn(system), string.Empty);

        return system;
    }

    // With the text console, asking through prompt.
    public static AgentLoop Loop(IAgentServer server, ISignInPrompt prompt, TestMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return Loop(server, Status(prompt, machine.Log), machine);
    }

    // Unless the machine brings a runner, the default one keeps status up to date too.
    public static AgentLoop Loop(IAgentServer server, ConsoleStatus status, TestMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        (FakeDeploymentTools tools, AgentLog log, TimeProvider time) = machine;

        return new(
            server,
            new AgentMachine(machine.Identity ?? new DryRunMachineIdentityReader(1), tools, new LocalRunLocator([tools.Volumes.Windows]), Version),
            status,
            machine.Runner ?? Runner(server, tools, log, time, new TestRunnerOptions { Status = status }),
            log,
            time);
    }
}

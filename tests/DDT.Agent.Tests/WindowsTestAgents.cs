// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

// TestAgents with the parts of Windows: the runner WinPE and the installed Windows have, and its hand-over.
internal static class WindowsTestAgents
{
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
        SequenceRunnerOptions runnerOptions = new(options.HeartbeatInterval ?? Timeout.InfiniteTimeSpan, Path.Combine(tools.Root, "X"), options.DryRun);

        return new SequenceRunnerBuilder
        {
            Server = server,
            Disks = tools,
            Partitioner = tools,
            FreeSpace = new DriveFreeSpace(),
            PreflightChecks =
            [
                new BootEnvironmentCheck(AgentEnvironment.WindowsPE),
                new BootImageToolsCheck(options.SystemDirectory ?? SystemDirectory(tools)),
                new ImageApplierCheck(tools),
            ],
            StepRunners = new WindowsStepRunners
            {
                Server = server,
                Partitioner = tools,
                Applier = tools,
                Tools = toolRunner,
                Joiner = options.Joiner ?? tools,
                Log = log,
                TimeProvider = timeProvider,
                Options = runnerOptions,
            },
            BcdWriter = options.BcdWriter ?? tools,
            BootOrder = tools,
            Rebooter = options.Rebooter ?? tools,
            RestartMarker = TestAgents.RestartMarker(tools, log),
            HandOver = HandOver(tools, toolRunner, log, options.DryRunHandOver, options.ConsoleDirectory),
            Log = log,
            TimeProvider = timeProvider,
            Options = runnerOptions,
            Status = options.Status,
            AccountTools = options.DryRun ? WindowsAccountTools.DryRun(log) : WindowsAccountTools.Create(log),
        }.Build();
    }

    // Stages AgentSource(tools) with TestAgents.Configuration, and the console in consoleDirectory.
    public static WindowsHandOver HandOver(
        FakeDeploymentTools tools,
        IToolRunner toolRunner,
        AgentLog log,
        bool dryRun = false,
        string? consoleDirectory = null) =>
        new(
            new OfflineServiceRegistration(toolRunner, log, dryRun),
            AgentSource(tools),
            TestAgents.Configuration,
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
        string powerShell = WindowsScriptLauncher.PowerShellIn(system);
        Directory.CreateDirectory(Path.GetDirectoryName(powerShell)!);
        File.WriteAllText(powerShell, string.Empty);
        File.WriteAllText(InjectDriversStepRunner.DismIn(system), string.Empty);

        return system;
    }

    // With the text console, asking through prompt.
    public static AgentLoop Loop(IAgentServer server, ISignInPrompt prompt, TestMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return Loop(server, TestAgents.Status(prompt, machine.Log), machine);
    }

    // Unless the machine brings a runner, it gets WinPE's, which keeps status up to date too.
    public static AgentLoop Loop(IAgentServer server, ConsoleStatus status, TestMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        (FakeDeploymentTools tools, AgentLog log, TimeProvider time) = machine;
        SequenceRunner runner = machine.Runner ?? Runner(server, tools, log, time, new TestRunnerOptions { Status = status });

        return TestAgents.Loop(server, status, machine with { Runner = runner });
    }
}

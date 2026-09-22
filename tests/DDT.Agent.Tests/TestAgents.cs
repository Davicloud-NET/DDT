// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;

namespace DDT.Agent.Tests;

internal static class TestAgents
{
    public const string Version = "1.0.0-test";

    public static AgentConfiguration Configuration { get; } =
        new("https://ddt.example:8443/", "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n", null);

    // Without a heartbeat interval, beats happen only when the run changes, so a run stays sequential on
    // ImmediateTimeProvider. Everything on the disk is under tools.Root: the volumes, Windows PE's own directory X,
    // and System32, where Windows PE's tools are, unless systemDirectory says otherwise. As in a dry run, unless dryRun
    // is false, the run's directory stays open to the account the tests run as.
    public static SequenceRunner Runner(
        IAgentServer server,
        FakeDeploymentTools tools,
        AgentLog log,
        TimeProvider timeProvider,
        TimeSpan? heartbeatInterval = null,
        IToolRunner? toolRunner = null,
        IBcdWriter? bcdWriter = null,
        IRebooter? rebooter = null,
        string? systemDirectory = null,
        bool dryRunHandOver = false,
        bool dryRun = true)
    {
        toolRunner ??= new RecordingToolRunner();

        return new SequenceRunner(
            server,
            tools,
            tools,
            bcdWriter ?? tools,
            rebooter ?? tools,
            toolRunner,
            HandOver(tools, toolRunner, log, dryRunHandOver),
            log,
            timeProvider,
            heartbeatInterval ?? Timeout.InfiniteTimeSpan,
            Path.Combine(tools.Root, "X"),
            systemDirectory ?? SystemDirectory(tools),
            dryRun);
    }

    // Stages AgentSource(tools) with Configuration.
    public static WindowsHandOver HandOver(FakeDeploymentTools tools, IToolRunner toolRunner, AgentLog log, bool dryRun = false) =>
        new(new OfflineServiceRegistration(toolRunner, log, dryRun), AgentSource(tools), Configuration, log, dryRun);

    // The running agent, as the self-update may have named it.
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

    // The service in the Windows on tools.Volumes.Windows, with the fake's setup, restart and removal.
    public static WindowsPhaseLoop WindowsLoop(
        IAgentServer server,
        FakeDeploymentTools tools,
        SequenceRunner runner,
        AgentLog log,
        TimeProvider timeProvider,
        bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(tools);

        return new WindowsPhaseLoop(
            server,
            new DryRunMachineIdentityReader(1),
            runner,
            tools,
            tools,
            tools,
            tools,
            log,
            timeProvider,
            Timeout.InfiniteTimeSpan,
            tools.Volumes.Windows,
            Version,
            dryRun);
    }

    public static AgentLoop Loop(
        IAgentServer server,
        ISignInPrompt prompt,
        FakeDeploymentTools tools,
        AgentLog log,
        TimeProvider timeProvider,
        IMachineIdentityReader? identity = null,
        SequenceRunner? runner = null) =>
        new(
            server,
            identity ?? new DryRunMachineIdentityReader(1),
            prompt,
            tools,
            runner ?? Runner(server, tools, log, timeProvider),
            new LocalRunLocator([tools.Volumes.Windows]),
            log,
            timeProvider,
            Version);
}

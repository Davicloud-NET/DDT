// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;

namespace DDT.Agent.Tests;

internal static class TestAgents
{
    public const string Version = "1.0.0-test";

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
        bool dryRun = true) =>
        new(
            server,
            tools,
            tools,
            bcdWriter ?? tools,
            rebooter ?? tools,
            toolRunner ?? new RecordingToolRunner(),
            log,
            timeProvider,
            heartbeatInterval ?? Timeout.InfiniteTimeSpan,
            Path.Combine(tools.Root, "X"),
            systemDirectory ?? SystemDirectory(tools),
            dryRun);

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

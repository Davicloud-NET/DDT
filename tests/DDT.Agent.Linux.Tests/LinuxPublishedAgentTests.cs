// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using DDT.Agent.LinuxPhase;
using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// The published agent itself, started as the unit starts it, with a folder as its / so that it finds no file of the
// computer the test runs on. Needs root for that, and the file build/Publish-LinuxAgent.ps1 made, which
// DDT_E2E_AGENT names as it does for the end-to-end tests.
[Trait("Category", "LinuxKernel")]
public sealed class LinuxPublishedAgentTests : IDisposable
{
    private const string AgentVariable = "DDT_E2E_AGENT";

    private const string StartId = "8f2c6f6e-0f5c-4d43-9d1d-4a1f0e0c2a01";

    private static readonly TimeSpan s_patience = TimeSpan.FromSeconds(60);

    private readonly string _root = Directory.CreateTempSubdirectory("ddt-published-agent-").FullName;
    private readonly List<string> _lines = [];
    private readonly Lock _lock = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private LinuxAgentPaths Paths => new(_root);

    private string[] Lines()
    {
        lock (_lock)
        {
            return [.. _lines];
        }
    }

    // The agent as the only program in the folder, with the two devices a program expects of any system.
    private void Install()
    {
        LoopDisk.SkipUnlessLinux();
        Assert.SkipUnless(Environment.UserName == "root", "Only root may give a program another folder as its /.");
        string? agent = Environment.GetEnvironmentVariable(AgentVariable);
        Assert.SkipUnless(File.Exists(agent), $"{AgentVariable} names no published agent.");

        File.Copy(agent ?? "", Path.Combine(_root, "ddt-agent"));
        Directory.CreateDirectory(Path.Combine(_root, "dev"));
        bool made = LoopDisk.Run("chmod", "755", Path.Combine(_root, "ddt-agent")).ExitCode == 0
            && LoopDisk.Run("mknod", Path.Combine(_root, "dev", "urandom"), "c", "1", "9").ExitCode == 0
            && LoopDisk.Run("mknod", Path.Combine(_root, "dev", "null"), "c", "1", "3").ExitCode == 0;
        Assert.SkipUnless(made, "This system lets the test make no device.");
    }

    private Process Start()
    {
        Process agent = new()
        {
            StartInfo = new ProcessStartInfo("chroot", [_root, "/ddt-agent", LinuxAgentFiles.Argument]) { RedirectStandardOutput = true, RedirectStandardError = true },
        };
        agent.OutputDataReceived += (_, line) => Keep(line.Data);
        agent.ErrorDataReceived += (_, line) => Keep(line.Data);
        agent.Start();
        agent.BeginOutputReadLine();
        agent.BeginErrorReadLine();

        return agent;
    }

    private void Keep(string? line)
    {
        if (line is not null)
        {
            lock (_lock)
            {
                _lines.Add(line);
            }
        }
    }

    private async Task WaitForLineAsync(string part)
    {
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(s_patience);

        while (!Lines().Any(line => line.Contains(part, StringComparison.Ordinal)))
        {
            await Task.Delay(50, limit.Token);
        }
    }

    // The argument alone decides that this is the unit's agent: without it the same file would ask for a server as
    // the agent of the boot environment does.
    [Fact]
    public async Task StartedWithTheUnitsArgumentAndNoSettingsItSaysSoAndEndsWithZero()
    {
        Install();

        using Process agent = Start();
        await agent.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, agent.ExitCode);
        Assert.EndsWith($"WARN  {LinuxPhaseService.NoSettings(LinuxAgentFiles.Configuration)}", Assert.Single(Lines()), StringComparison.Ordinal);
    }

    // systemd stops the unit with SIGTERM. The agent, which cannot reach its server here, ends within the time
    // systemd gives it, says so, and ends with 0. Its note of the start and its log are root's alone.
    [Fact]
    public async Task StoppedAsSystemdStopsItTheAgentSaysSoAndEndsWithZero()
    {
        Install();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RunFiles files = RunFiles.In(Paths.State, new AgentLog(TimeProvider.System, TextWriter.Null));
        SequenceDefinition definition = new(
            SequenceDefinition.CurrentVersion,
            [new RunScriptStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000c001"), Name = "Script", Phase = SequencePhase.Linux, Interpreter = ScriptInterpreter.Shell, Script = "true" }]);
        await files.SaveStateAsync(SequenceStates.Start(Guid.Parse("0193a4b2-0000-7000-8000-0000000000f1"), definition) with { Phase = SequencePhase.Linux }, cancellationToken);
        await files.SaveTokenAsync("made-up-token-of-the-test", cancellationToken);
        await File.WriteAllTextAsync(Paths.Configuration, """{"serverUrl":"https://127.0.0.1:9/"}""", cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.At(CloudInitFiles.StartIdPath))!);
        await File.WriteAllTextAsync(Paths.At(CloudInitFiles.StartIdPath), StartId + "\n", cancellationToken);

        using Process agent = Start();
        await WaitForLineAsync("Cannot register with the server (");
        Assert.Equal(0, LoopDisk.Run("kill", "-TERM", $"{agent.Id}").ExitCode);
        await agent.WaitForExitAsync(cancellationToken).WaitAsync(s_patience, cancellationToken);

        Assert.Equal(0, agent.ExitCode);
        Assert.EndsWith("INFO  The agent was stopped. The run goes on when the service starts again.", Lines()[^1], StringComparison.Ordinal);
        Assert.Equal(StartId, await File.ReadAllTextAsync(Paths.FirstStart, cancellationToken));
        Assert.Equal("600", LoopDisk.Run("stat", "-c", "%a", Paths.FirstStart).Output);
        Assert.Equal("600", LoopDisk.Run("stat", "-c", "%a", Paths.LogFile).Output);
        Assert.Equal(Lines().Length, (await File.ReadAllLinesAsync(Paths.LogFile, cancellationToken)).Length);
        Assert.DoesNotContain("made-up-token", string.Join('\n', Lines()), StringComparison.Ordinal);
    }
}

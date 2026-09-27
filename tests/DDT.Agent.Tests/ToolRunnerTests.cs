// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ToolRunnerTests
{
    private static readonly string s_cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task LogsTheCommandItsOutputAndItsExitCode()
    {
        (ToolRunner tools, ScriptedAgentServer server, AgentLog log) = Create();

        IReadOnlyList<string> output = await tools.RunAsync(s_cmd, ["/c", "echo out& echo err 1>&2"], TestContext.Current.CancellationToken);

        Assert.Equal(["out"], output);

        List<AgentLogLine> lines = await SentAsync(server, log);
        Assert.Equal($"Running {s_cmd} /c \"echo out& echo err 1>&2\"", lines[0].Message);
        Assert.Contains(lines, line => line is { Level: AgentLogLevel.Information, Message: "out" });
        Assert.Contains(lines, line => line is { Level: AgentLogLevel.Warning, Message: "err" });
        Assert.StartsWith("cmd.exe ended with exit code 0x00000000 after ", lines[^1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailsOnANonZeroExitCodeNamingTheToolAndTheCode()
    {
        (ToolRunner tools, ScriptedAgentServer server, AgentLog log) = Create();

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => tools.RunAsync(s_cmd, ["/c", "exit /b 5"], TestContext.Current.CancellationToken));

        Assert.Equal("cmd.exe failed with exit code 0x00000005. Its output is in the machine log.", exception.Message);
        Assert.Contains(await SentAsync(server, log), line => line.Message.StartsWith("cmd.exe ended with exit code 0x00000005", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailsWhenTheToolIsMissing()
    {
        (ToolRunner tools, _, _) = Create();
        string missing = Path.Combine(Path.GetTempPath(), $"ddt-missing-{Guid.NewGuid():N}.exe");

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => tools.RunAsync(missing, [], TestContext.Current.CancellationToken));

        Assert.StartsWith($"{missing} cannot be started", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandsBackAnyExitCodeAndLogsTheOutput()
    {
        (ToolRunner tools, ScriptedAgentServer server, AgentLog log) = Create();

        int exitCode = await tools.RunForExitCodeAsync(
            s_cmd,
            ["/d", "/c", "echo out& echo err 1>&2& exit /b 3010"],
            new ToolRunOptions(),
            TestContext.Current.CancellationToken);

        Assert.Equal(3010, exitCode);

        List<AgentLogLine> lines = await SentAsync(server, log);
        Assert.Contains(lines, line => line is { Level: AgentLogLevel.Information, Message: "out" });
        Assert.Contains(lines, line => line is { Level: AgentLogLevel.Warning, Message: "err" });
        Assert.StartsWith("cmd.exe ended with exit code 3010 after ", lines[^1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunsInTheWorkingDirectoryWithTheExtraVariables()
    {
        (ToolRunner tools, ScriptedAgentServer server, AgentLog log) = Create();
        string directory = Directory.CreateTempSubdirectory("ddt-tool-").FullName;

        try
        {
            int exitCode = await tools.RunForExitCodeAsync(
                s_cmd,
                ["/d", "/c", "cd& echo %DDT_STEP_ID%"],
                new ToolRunOptions(directory, new Dictionary<string, string> { ["DDT_STEP_ID"] = "step-1" }),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, exitCode);

            List<AgentLogLine> lines = await SentAsync(server, log);
            Assert.Contains(lines, line => line.Message == directory);
            Assert.Contains(lines, line => line.Message == "step-1");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // outer.cmd starts inner.cmd, which holds held.txt open for half a minute unless it was stopped with outer.cmd.
    [Fact]
    public async Task StopsTheToolAndEverythingItStartedAtTheTimeout()
    {
        (ToolRunner tools, ScriptedAgentServer server, AgentLog log) = Create();
        string directory = Directory.CreateTempSubdirectory("ddt-tool-").FullName;
        string held = Path.Combine(directory, "held.txt");
        await File.WriteAllTextAsync(
            Path.Combine(directory, "outer.cmd"),
            "@echo waiting for inner.cmd\r\n@cmd /d /c \"%~dp0inner.cmd\"\r\n",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "inner.cmd"),
            "@ping -n 30 127.0.0.1 >\"%~dp0held.txt\"\r\n",
            TestContext.Current.CancellationToken);

        try
        {
            DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(() => tools.RunForExitCodeAsync(
                s_cmd,
                ["/d", "/c", Path.Combine(directory, "outer.cmd")],
                new ToolRunOptions(Timeout: TimeSpan.FromMilliseconds(500)),
                TestContext.Current.CancellationToken));

            Assert.StartsWith("cmd.exe was still running after ", exception.Message, StringComparison.Ordinal);
            Assert.EndsWith(", so it was stopped with every process it started.", exception.Message, StringComparison.Ordinal);

            // What it printed before it hung says why.
            Assert.Contains(await SentAsync(server, log), line => line.Message == "waiting for inner.cmd");

            // A killed process ends a moment later, and a busy machine may stop the tool late, so this waits for the
            // file to be free rather than for a fixed time. A process that escaped the stop holds it for half a minute.
            Assert.True(await NothingHoldsAsync(held, TimeSpan.FromSeconds(10)), "A process outer.cmd started still holds held.txt.");
        }
        finally
        {
            // Where a process escaped, it still holds its file, and the failure above says so rather than this.
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task AStopEndsTheToolAsCancelled()
    {
        (ToolRunner tools, _, _) = Create();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.RunForExitCodeAsync(
            s_cmd,
            ["/d", "/c", "ping -n 30 127.0.0.1 >nul"],
            new ToolRunOptions(Timeout: TimeSpan.FromMinutes(5)),
            stop.Token));
    }

    private static (ToolRunner Tools, ScriptedAgentServer Server, AgentLog Log) Create()
    {
        AgentLog log = new(new ImmediateTimeProvider(), TextWriter.Null);

        return (new ToolRunner(log, TimeProvider.System), new ScriptedAgentServer(), log);
    }

    // True once the file opens for this process alone, or is not there. A process that never started leaves no file.
    private static async Task<bool> NothingHoldsAsync(string path, TimeSpan wait)
    {
        long started = Stopwatch.GetTimestamp();

        while (true)
        {
            try
            {
                using FileStream file = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

                return true;
            }
            catch (FileNotFoundException)
            {
                return true;
            }
            catch (IOException) when (Stopwatch.GetElapsedTime(started) < wait)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    private static async Task<List<AgentLogLine>> SentAsync(ScriptedAgentServer server, AgentLog log)
    {
        while (log.QueuedLines > 0)
        {
            await log.FlushAsync(server, Guid.Empty, "token", TestContext.Current.CancellationToken);
        }

        return server.SentLines;
    }
}

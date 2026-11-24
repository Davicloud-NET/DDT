// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

// A tool that prints a secret, as manage-bde prints a recovery password, hands its lines to the caller only. The secret
// comes in through the environment, so the logged command line doesn't name it.
public sealed class ToolRunnerPrivateOutputTests
{
    private const string Secret = "111111-222222-333333-444444-555555-666666-777777-888888";

    private static readonly string s_cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static readonly Dictionary<string, string> s_environment = new(StringComparer.Ordinal) { ["DDT_SECRET"] = Secret };

    [Fact]
    public async Task TheCallerGetsTheLinesAndNeitherLogDoes()
    {
        using StringWriter console = new();
        AgentLog log = new(new ImmediateTimeProvider(), console);
        ToolRunner tools = new(log, TimeProvider.System, AnsiCodePage.Decode);
        PrivateOutput output = new();

        int exitCode = await tools.RunForExitCodeAsync(
            s_cmd,
            ["/d", "/c", "echo %DDT_SECRET%& echo %DDT_SECRET% 1>&2"],
            new ToolRunOptions(Environment: s_environment, Private: output),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal([Secret], output.Lines);
        Assert.Equal([Secret], output.Errors);
        List<AgentLogLine> sent = await SentAsync(log);
        Assert.DoesNotContain(sent, line => line.Message.Contains(Secret, StringComparison.Ordinal));
        Assert.EndsWith("Its output is not logged.", sent[^1].Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailingToolKeepsItsOutputOutOfTheLogToo()
    {
        using StringWriter console = new();
        AgentLog log = new(new ImmediateTimeProvider(), console);
        ToolRunner tools = new(log, TimeProvider.System, AnsiCodePage.Decode);
        PrivateOutput output = new();

        int exitCode = await tools.RunForExitCodeAsync(
            s_cmd,
            ["/d", "/c", "echo %DDT_SECRET%& exit /b 3"],
            new ToolRunOptions(Environment: s_environment, Private: output),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, exitCode);
        Assert.Equal([Secret], output.Lines);
        Assert.DoesNotContain(await SentAsync(log), line => line.Message.Contains(Secret, StringComparison.Ordinal));
        Assert.DoesNotContain(Secret, console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AQuietToolThatSucceedsLeavesNoLine()
    {
        AgentLog log = new(new ImmediateTimeProvider(), TextWriter.Null);
        ToolRunner tools = new(log, TimeProvider.System, AnsiCodePage.Decode);

        int exitCode = await tools.RunForExitCodeAsync(
            s_cmd,
            ["/d", "/c", "echo status:2 74 0"],
            new ToolRunOptions(Private: new PrivateOutput(), Quiet: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Empty(await SentAsync(log));
    }

    [Fact]
    public async Task AQuietToolThatFailsIsLoggedAsAnyOther()
    {
        AgentLog log = new(new ImmediateTimeProvider(), TextWriter.Null);
        ToolRunner tools = new(log, TimeProvider.System, AnsiCodePage.Decode);

        int exitCode = await tools.RunForExitCodeAsync(
            s_cmd,
            ["/d", "/c", "exit /b 1"],
            new ToolRunOptions(Private: new PrivateOutput(), Quiet: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        List<AgentLogLine> sent = await SentAsync(log);
        Assert.Equal(2, sent.Count);
        Assert.StartsWith($"Running {s_cmd}", sent[0].Message, StringComparison.Ordinal);
        Assert.StartsWith("cmd.exe ended with exit code 1 after", sent[1].Message, StringComparison.Ordinal);
    }

    private static async Task<List<AgentLogLine>> SentAsync(AgentLog log)
    {
        ScriptedAgentServer server = new();

        while (log.QueuedLines > 0)
        {
            await log.FlushAsync(server, Guid.Empty, "token", TestContext.Current.CancellationToken);
        }

        return server.SentLines;
    }
}

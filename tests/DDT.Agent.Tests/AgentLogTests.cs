// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class AgentLogTests
{
    private const int QueueCapacity = 2000;

    private static readonly Guid s_machineId = Guid.Parse("0193a4b2-0000-7000-8000-000000000001");

    [Fact]
    public async Task ReportsDroppedLinesUntilTheReportIsDelivered()
    {
        AgentLog log = new(new ImmediateTimeProvider(), TextWriter.Null);

        for (int line = 0; line < QueueCapacity + 5; line++)
        {
            log.Information($"line {line}");
        }

        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnLog(_ => throw new HttpRequestException("503"));

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<HttpRequestException>(() => log.FlushAsync(server, s_machineId, "token", cancellationToken));
        await log.FlushAsync(server, s_machineId, "token", cancellationToken);

        Assert.Equal("5 log lines were dropped while the server could not take them.", server.SentLines[0].Message);
        Assert.Equal("line 5", server.SentLines[1].Message);
        Assert.Equal(AgentLimits.MaxLinesPerBatch, server.SentLines.Count);

        server.SentLines.Clear();
        await log.FlushAsync(server, s_machineId, "token", cancellationToken);

        Assert.Equal("line 204", server.SentLines[0].Message);
    }

    [Fact]
    public async Task KeepsLinesWrittenWhileABatchIsOnItsWay()
    {
        AgentLog log = new(new ImmediateTimeProvider(), TextWriter.Null);

        for (int line = 0; line < QueueCapacity; line++)
        {
            log.Information($"a{line}");
        }

        // Ten lines arrive during the request and push the ten oldest out of the full queue.
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnLog(_ =>
            {
                for (int line = 0; line < 10; line++)
                {
                    log.Information($"b{line}");
                }
            });

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        while (log.QueuedLines > 0)
        {
            await log.FlushAsync(server, s_machineId, "token", cancellationToken);
        }

        HashSet<string> sent = [.. server.SentLines.Select(line => line.Message)];
        Assert.All(Enumerable.Range(AgentLimits.MaxLinesPerBatch, QueueCapacity - AgentLimits.MaxLinesPerBatch), line => Assert.Contains($"a{line}", sent));
        Assert.All(Enumerable.Range(0, 10), line => Assert.Contains($"b{line}", sent));
    }

    [Fact]
    public void KeepsLinesOffTheConsoleWhileSomeoneTypes()
    {
        using StringWriter console = new();
        AgentLog log = new(new ImmediateTimeProvider(), console);

        log.HoldConsole("User name: ");
        log.Warning("Cannot reach the server.");

        Assert.Equal("User name: ", console.ToString());

        log.ReleaseConsole();

        Assert.Equal($"User name: 00:00:00 WARN  Cannot reach the server.{Environment.NewLine}", console.ToString());
    }

    // agent.log in the installed Windows is read days later, beside setupact.log and the event log.
    [Fact]
    public void DatesTheLinesOfAFileAndKeepsTheConsoleShort()
    {
        ManualTimeProvider time = new();
        time.Advance(new TimeSpan(265, 14, 3, 12));
        using StringWriter file = new();
        using StringWriter console = new();

        new AgentLog(time, file, datedLines: true).Information("The run is done.");
        new AgentLog(time, console).Information("The run is done.");

        Assert.Equal($"2026-09-23 14:03:12 UTC INFO  The run is done.{Environment.NewLine}", file.ToString());
        Assert.Equal($"14:03:12 INFO  The run is done.{Environment.NewLine}", console.ToString());
    }

    [Fact]
    public async Task SendsItsClockWithEachBatch()
    {
        ManualTimeProvider time = new();
        AgentLog log = new(time, TextWriter.Null);
        DateTimeOffset written = time.GetUtcNow();
        log.Information("Partitioning disk 0.");
        time.Advance(TimeSpan.FromMinutes(5));
        AgentLogBatch? sent = null;
        ScriptedAgentServer server = new ScriptedAgentServer().OnLog(batch => sent = batch);

        await log.FlushAsync(server, s_machineId, "token", TestContext.Current.CancellationToken);

        Assert.NotNull(sent);
        Assert.Equal(time.GetUtcNow(), sent.SentUtc);
        Assert.Equal(written, Assert.Single(sent.Lines).TimestampUtc);
    }

    [Fact]
    public async Task NamesTheStepThatRanWhenALineWasWritten()
    {
        Guid step = Guid.Parse("0197a3c0-0000-7000-8000-00000000000c");
        AgentLog log = new(new ImmediateTimeProvider(), TextWriter.Null);
        ScriptedAgentServer server = new();

        log.Information("Before the step.");
        log.StepId = step;
        log.Warning("During the step.");
        log.StepId = null;
        log.Information("After the step.");
        await log.FlushAsync(server, s_machineId, "token", TestContext.Current.CancellationToken);

        Assert.Equal([null, step, null], server.SentLines.Select(line => line.StepId));
    }

    [Fact]
    public async Task SendsNothingWhenNothingIsQueued()
    {
        AgentLog log = new(new ImmediateTimeProvider(), TextWriter.Null);
        ScriptedAgentServer server = new();

        await log.FlushAsync(server, s_machineId, "token", TestContext.Current.CancellationToken);

        Assert.Empty(server.Calls);
    }
}

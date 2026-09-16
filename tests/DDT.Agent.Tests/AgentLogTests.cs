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
    public async Task SendsNothingWhenNothingIsQueued()
    {
        AgentLog log = new(new ImmediateTimeProvider(), TextWriter.Null);
        ScriptedAgentServer server = new();

        await log.FlushAsync(server, s_machineId, "token", TestContext.Current.CancellationToken);

        Assert.Empty(server.Calls);
    }
}

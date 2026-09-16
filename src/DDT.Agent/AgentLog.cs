using System.Globalization;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// Writes to the console immediately, because in Windows PE the console is all an operator at the
// machine has, and queues the same lines for the server until they are delivered.
public sealed class AgentLog(TimeProvider timeProvider, TextWriter console)
{
    private const int MaxQueuedLines = 2000;

    private readonly Lock _lock = new();
    private readonly List<(long Sequence, AgentLogLine Line)> _pending = [];
    private long _nextSequence;
    private int _dropped;

    public void Information(string message) => Write(AgentLogLevel.Information, message);

    public void Warning(string message) => Write(AgentLogLevel.Warning, message);

    public void Error(string message) => Write(AgentLogLevel.Error, message);

    public int QueuedLines
    {
        get
        {
            lock (_lock)
            {
                return _pending.Count;
            }
        }
    }

    public async Task FlushAsync(IAgentServer server, Guid machineId, string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);

        List<AgentLogLine> batch = [];
        long lastSequence;
        int dropped;

        lock (_lock)
        {
            dropped = _dropped;

            // The count of lost lines travels in the batch rather than the queue, so it cannot itself be
            // evicted, and it is only cleared once a batch carrying it has been accepted.
            if (dropped > 0)
            {
                batch.Add(new AgentLogLine(
                    timeProvider.GetUtcNow(),
                    AgentLogLevel.Warning,
                    $"{dropped} log lines were dropped while the server could not take them."));
            }

            int take = AgentLimits.MaxLinesPerBatch - batch.Count;
            batch.AddRange(_pending.Take(take).Select(entry => entry.Line));
            lastSequence = _pending.Take(take).Select(entry => entry.Sequence).DefaultIfEmpty(-1).Last();
        }

        if (batch.Count == 0)
        {
            return;
        }

        await server.SendLogAsync(machineId, token, new AgentLogBatch(batch), cancellationToken).ConfigureAwait(false);

        // Removed by sequence, not position: lines evicted while the batch was on its way have shifted the
        // queue, and must not take unsent lines with them.
        lock (_lock)
        {
            _pending.RemoveAll(entry => entry.Sequence <= lastSequence);
            _dropped -= dropped;
        }
    }

    private void Write(AgentLogLevel level, string message)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{now:HH:mm:ss} {LevelLabel(level)} {message}"));

        lock (_lock)
        {
            if (_pending.Count >= MaxQueuedLines)
            {
                _pending.RemoveAt(0);
                _dropped++;
            }

            _pending.Add((_nextSequence++, new AgentLogLine(now, level, message)));
        }
    }

    private static string LevelLabel(AgentLogLevel level) => level switch
    {
        AgentLogLevel.Warning => "WARN ",
        AgentLogLevel.Error => "ERROR",
        _ => "INFO ",
    };
}

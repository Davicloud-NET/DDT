using DDT.Contracts.Machines;
using DDT.Server.Machines;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Live;

// Pushes are not awaited by the request that caused them: a browser that stops reading would otherwise
// hold up an agent's poll or an operator's approval.
public sealed partial class LiveNotifier(IHubContext<LiveHub> hub, ILogger<LiveNotifier> logger)
{
    public void MachineChanged(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        _ = PushAsync(MachineSummaries.From(machine));
    }

    private async Task PushAsync(MachineSummary summary)
    {
        try
        {
            await hub.Clients.All.SendAsync(LiveEvents.MachineChanged, summary, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogPushFailed(summary.Id, exception);
        }
    }

    [LoggerMessage(EventId = 400, Level = LogLevel.Warning, Message = "Could not push the change to machine {MachineId}")]
    private partial void LogPushFailed(Guid machineId, Exception exception);
}

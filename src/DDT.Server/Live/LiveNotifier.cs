using DDT.Contracts.Machines;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Live;

// Pushes are not awaited by the request that caused them: a browser that stops reading would otherwise
// hold up an agent's poll or an operator's approval.
public sealed partial class LiveNotifier(IHubContext<LiveHub> hub, ILogger<LiveNotifier> logger)
{
    // The deployment the Machines page shows for this machine, see MachineSummaries.From.
    public void MachineChanged(Machine machine, Deployment? deployment)
    {
        ArgumentNullException.ThrowIfNull(machine);

        _ = PushAsync(MachineSummaries.From(machine, deployment));
    }

    public void MachinesRemoved() => _ = PushEventAsync(LiveEvents.MachinesRemoved);

    public void ImagesChanged() => _ = PushEventAsync(LiveEvents.ImagesChanged);

    private async Task PushEventAsync(string liveEvent)
    {
        try
        {
            await hub.Clients.All.SendAsync(liveEvent, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogEventPushFailed(liveEvent, exception);
        }
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

    [LoggerMessage(EventId = 430, Level = LogLevel.Warning, Message = "Could not push the change to machine {MachineId}")]
    private partial void LogPushFailed(Guid machineId, Exception exception);

    [LoggerMessage(EventId = 431, Level = LogLevel.Warning, Message = "Could not push the event {LiveEvent}")]
    private partial void LogEventPushFailed(string liveEvent, Exception exception);
}

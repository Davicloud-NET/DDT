namespace DDT.Agent.Deployment;

public sealed class DryRunRebooter(AgentLog log) : IRebooter
{
    public Task RebootAsync(CancellationToken cancellationToken)
    {
        log.Information("Dry run: this computer is not restarted. In Windows PE, wpeutil reboot would run now.");

        return Task.CompletedTask;
    }
}

namespace DDT.Agent.Deployment;

// The agent's exit does not restart Windows PE: startnet.cmd leaves a command prompt open.
public sealed class WpeutilRebooter(ToolRunner tools) : IRebooter
{
    public Task RebootAsync(CancellationToken cancellationToken) =>
        tools.RunAsync(Path.Combine(Environment.SystemDirectory, "wpeutil.exe"), ["reboot"], cancellationToken);
}

namespace DDT.Agent.Deployment;

public sealed class DryRunBcdWriter(AgentLog log) : IBcdWriter
{
    public Task WriteAsync(TargetVolumes volumes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(volumes);

        string windows = Path.Combine(volumes.Windows, "Windows");
        string recovery = Path.Combine(volumes.Recovery, "Recovery", "WindowsRE");

        log.Information("Dry run: nothing is made bootable. These would run:");
        log.Information($"  {ToolRunner.CommandLine(Path.Combine(windows, "System32", "bcdboot.exe"), [windows, "/s", volumes.System, "/f", "UEFI"])}");
        log.Information($"  {ToolRunner.CommandLine(Path.Combine(Environment.SystemDirectory, "bcdedit.exe"), ["/enum", "firmware"])}");
        log.Information($"  {ToolRunner.CommandLine(Path.Combine(windows, "System32", "reagentc.exe"), ["/setreimage", "/path", recovery, "/target", windows])}");

        return Task.CompletedTask;
    }
}

namespace DDT.Agent.Deployment;

// Microsoft's sequence after applying: the applied image's own bcdboot and reagentc, which match its version.
public sealed class BcdbootWriter(ToolRunner tools, AgentLog log) : IBcdWriter
{
    public async Task WriteAsync(TargetVolumes volumes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(volumes);

        string windows = Path.Combine(volumes.Windows, "Windows");
        string system32 = Path.Combine(windows, "System32");

        await tools.RunAsync(
            Path.Combine(system32, "bcdboot.exe"),
            [windows, "/s", volumes.System.TrimEnd('\\'), "/f", "UEFI"],
            cancellationToken).ConfigureAwait(false);

        // Only for the log: whether Windows Boot Manager now comes before the network decides if the machine
        // starts Windows or netboots again, and a real PC gives no other way to see it.
        try
        {
            await tools.RunAsync(Path.Combine(Environment.SystemDirectory, "bcdedit.exe"), ["/enum", "firmware"], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"The firmware boot entries cannot be listed ({exception.Message}).");
        }

        string winre = Path.Combine(system32, "Recovery", "Winre.wim");

        if (!File.Exists(winre))
        {
            log.Warning($"{winre} is missing, so this Windows gets no recovery environment. Deploy an image that contains it to have one.");

            return;
        }

        string recovery = Path.Combine(volumes.Recovery, "Recovery", "WindowsRE");
        Directory.CreateDirectory(recovery);
        File.Copy(winre, Path.Combine(recovery, "Winre.wim"), overwrite: true);

        await tools.RunAsync(
            Path.Combine(system32, "reagentc.exe"),
            ["/setreimage", "/path", recovery, "/target", windows],
            cancellationToken).ConfigureAwait(false);
    }
}

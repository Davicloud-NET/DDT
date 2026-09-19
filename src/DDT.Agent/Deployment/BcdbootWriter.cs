namespace DDT.Agent.Deployment;

// Microsoft's sequence after applying: the applied image's own bcdboot and reagentc, which match its version.
public sealed class BcdbootWriter(ToolRunner tools, AgentLog log) : IBcdWriter
{
    private static readonly string s_bcdedit = Path.Combine(Environment.SystemDirectory, "bcdedit.exe");

    public async Task WriteAsync(TargetVolumes volumes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(volumes);

        string windows = Path.Combine(volumes.Windows, "Windows");
        string system32 = Path.Combine(windows, "System32");
        string bcdboot = Path.Combine(system32, "bcdboot.exe");

        await tools.RunAsync(bcdboot, [windows, "/s", volumes.System.TrimEnd('\\'), "/f", "UEFI"], cancellationToken)
            .ConfigureAwait(false);

        await PutWindowsFirstAsync(bcdboot, windows, cancellationToken).ConfigureAwait(false);

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

    // A machine that starts from the network first would otherwise netboot again instead of starting Windows.
    // bcdboot with /s may leave the firmware alone; without /s it adds the Windows Boot Manager entry itself.
    private async Task PutWindowsFirstAsync(string bcdboot, string windows, CancellationToken cancellationToken)
    {
        string? entry = await FindWindowsBootManagerAsync(cancellationToken).ConfigureAwait(false);

        if (entry is null)
        {
            try
            {
                await tools.RunAsync(bcdboot, [windows], cancellationToken).ConfigureAwait(false);
            }
            catch (DeploymentStepException exception)
            {
                log.Warning($"bcdboot could not add a firmware boot entry ({exception.Message}).");
            }

            entry = await FindWindowsBootManagerAsync(cancellationToken).ConfigureAwait(false);
        }

        if (entry is null)
        {
            log.Warning("The firmware has no Windows Boot Manager entry, so this machine may start from the network again. Set its boot order to start Windows Boot Manager first.");

            return;
        }

        try
        {
            await tools.RunAsync(s_bcdedit, ["/set", "{fwbootmgr}", "displayorder", entry, "/addfirst"], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"Windows Boot Manager could not be moved to the front of the boot order ({exception.Message}). Set the boot order by hand if this machine starts from the network again.");
        }
    }

    private async Task<string?> FindWindowsBootManagerAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<string> listing = await tools.RunAsync(s_bcdedit, ["/enum", "firmware"], cancellationToken).ConfigureAwait(false);

            return FirmwareBootEntries.FindWindowsBootManager(listing);
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"The firmware boot entries cannot be listed ({exception.Message}).");

            return null;
        }
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Follows Microsoft's steps after applying an image, with the bcdboot and reagentc from the image, because they match
// its version. bcdboot always gets /s. Without it, bcdboot may write to another disk's EFI system partition, which
// nobody confirmed erasing. After a change to the boot order, bcdedit lists it for the machine log.
public sealed class BcdbootWriter(IToolRunner tools, IUefiVariables variables, AgentLog log) : IBcdWriter, IBootOrder
{
    private static readonly string s_bcdedit = Path.Combine(Environment.SystemDirectory, "bcdedit.exe");

    private readonly FirmwareBootOrder _bootOrder = new(variables, log);

    public async Task WriteAsync(TargetVolumes volumes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(volumes);

        // Every run comes here first.
        _bootOrder.Forget();

        string windows = Path.Combine(volumes.Windows, "Windows");
        string system32 = Path.Combine(windows, "System32");

        await tools.RunAsync(Path.Combine(system32, "bcdboot.exe"), [windows, "/s", volumes.System.TrimEnd('\\'), "/f", "UEFI"], cancellationToken)
            .ConfigureAwait(false);

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

    // bcdboot with /s doesn't reliably touch the firmware's boot entries. On Hyper-V it put its entry first, and the
    // documentation says /s is for another machine's disk. So the boot variables are checked and written here.
    public async Task PutWindowsFirstAsync(TargetVolumes volumes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(volumes);

        try
        {
            _bootOrder.MakeFirst(
                () => EspReader.Read(volumes.System),
                FirmwareBootEntry.WindowsLoaderPath,
                FirmwareBootEntry.WindowsDescription,
                volumes.ErasedSystemPartitionIds);
        }
        catch (Exception exception)
        {
            log.Warning(
                $"Windows Boot Manager could not be put first in the firmware boot order ({exception.Message}), so this machine may " +
                "start from the network again. Set its boot order to start Windows Boot Manager first.");
        }

        await ListFirmwareEntriesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PutFirstAsync(
        EspPartition esp,
        string loaderPath,
        string description,
        IReadOnlyCollection<Guid> erasedSystemPartitionIds,
        CancellationToken cancellationToken)
    {
        await _bootOrder.PutFirstAsync(esp, loaderPath, description, erasedSystemPartitionIds, cancellationToken).ConfigureAwait(false);
        await ListFirmwareEntriesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task RestoreAsync(CancellationToken cancellationToken) => _bootOrder.RestoreAsync(cancellationToken);

    private async Task ListFirmwareEntriesAsync(CancellationToken cancellationToken)
    {
        // Only for the machine log, which then shows the order the firmware has.
        try
        {
            await tools.RunAsync(s_bcdedit, ["/enum", "firmware"], cancellationToken).ConfigureAwait(false);
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"The firmware boot entries cannot be listed ({exception.Message}).");
        }
    }
}

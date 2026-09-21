// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Microsoft's sequence after applying: the applied image's own bcdboot and reagentc, which match its version.
// bcdboot always gets /s: without it, it may write to the EFI system partition of another disk, which the technician
// never confirmed erasing.
public sealed class BcdbootWriter(IToolRunner tools, IUefiVariables variables, AgentLog log) : IBcdWriter
{
    private static readonly string s_bcdedit = Path.Combine(Environment.SystemDirectory, "bcdedit.exe");

    // What PutWindowsFirstAsync changed in the current run.
    private UndoableUefiVariables? _changes;

    public async Task WriteAsync(TargetVolumes volumes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(volumes);

        // Every run comes here first, so nothing an earlier run changed is ever put back.
        _changes = null;

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

    // bcdboot with /s does not reliably touch the firmware's boot entries (on Hyper-V it put its entry first, and the
    // documentation says it is for another machine's disk), so the boot variables are checked and written here.
    public async Task PutWindowsFirstAsync(TargetVolumes volumes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(volumes);

        UndoableUefiVariables changes = new(variables);
        _changes = changes;

        try
        {
            WindowsBootEntry.MakeFirst(changes, EspReader.Read(volumes.System), volumes.ErasedSystemPartitionIds, log);
        }
        catch (Exception exception)
        {
            log.Warning(
                $"Windows Boot Manager could not be put first in the firmware boot order ({exception.Message}), so this machine may " +
                "start from the network again. Set its boot order to start Windows Boot Manager first.");
        }

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

    public Task RestoreBootOrderAsync(CancellationToken cancellationToken)
    {
        UndoableUefiVariables? changes = _changes;
        _changes = null;

        try
        {
            if (changes is not null && changes.Undo())
            {
                log.Information("The firmware boot order is back as it was before the deployment, so this machine does not start Windows without its answer file.");
            }
        }
        catch (Exception exception)
        {
            log.Warning(
                $"The firmware boot order could not be put back as it was before the deployment ({exception.Message}), so this machine may " +
                "start Windows without its answer file. Start it from the network to deploy it again.");
        }

        return Task.CompletedTask;
    }
}

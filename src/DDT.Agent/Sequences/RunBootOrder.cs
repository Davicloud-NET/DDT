// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// Changes the firmware's boot order for a run. Until the last change, a restart starts the machine from the network
// rather than a system that isn't ready. From then on a restart into the installed system is due.
internal sealed class RunBootOrder(IBcdWriter bcdWriter, WindowsPERestartMarker restartMarker, AgentLog log)
{
    // Firmware setup screens show a line of this much.
    private const int MaxBootEntryName = 64;

    // Follows Microsoft's steps after applying: bcdboot with the recovery environment, then Windows Boot Manager first.
    // handOverRun comes before bcdboot, because bcdboot may put its entry first itself. That way a failed hand-over has
    // nothing to undo.
    public async Task MakeBootableAsync(SequenceRun run, TargetVolumes volumes, Func<Task>? handOverRun, CancellationToken cancellationToken)
    {
        if (handOverRun is not null)
        {
            await handOverRun().ConfigureAwait(false);
        }

        await bcdWriter.WriteAsync(volumes, cancellationToken).ConfigureAwait(false);
        run.WindowsFirst = true;
        await bcdWriter.PutWindowsFirstAsync(volumes, cancellationToken).ConfigureAwait(false);
        restartMarker.Set(RestartInto.Windows);
    }

    // A raw disk image starts from its fallback loader, under a boot entry named after the image. An image without an
    // EFI system partition gets none.
    public async Task PutRawImageFirstAsync(SequenceRun run, CancellationToken cancellationToken)
    {
        SequenceState state = run.State;

        if (RunVariables.RawSystemPartitionOf(state.Variables) is not { } esp)
        {
            log.Warning(
                "The disk image has no EFI system partition, so the machine gets no boot entry for it and starts it only if its " +
                "firmware tries the disk. Set its boot order to start the disk first.");

            return;
        }

        string description = BootEntryName(run);
        run.WindowsFirst = true;
        await bcdWriter.PutFirstAsync(
            esp,
            FirmwareBootEntry.FallbackLoaderPath,
            description,
            RunVariables.ErasedSystemPartitionIdsOf(state.Variables),
            cancellationToken).ConfigureAwait(false);
        restartMarker.Set(RestartInto.Windows);
    }

    // With the boot order restored, a restart starts from the network, so no restart into the installed system is due.
    public async Task RestoreAsync(SequenceRun run)
    {
        if (run.WindowsFirst)
        {
            run.WindowsFirst = false;
            restartMarker.Clear();
            await bcdWriter.RestoreBootOrderAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    // In a tree, the image of the branch the run took: the one whose step is done.
    private static string BootEntryName(SequenceRun run)
    {
        SequenceState state = run.State;
        HashSet<Guid> done = [.. state.Steps.Where(step => step.State == StepState.Done).Select(step => step.StepId)];
        string? name = SequenceTree.Nodes(state.Definition).OfType<WriteRawImageStep>()
            .OrderByDescending(step => done.Contains(step.Id))
            .Select(step => run.Session.Run.Images.FirstOrDefault(image => image.ImageId == step.ImageId)?.Name)
            .FirstOrDefault(found => !string.IsNullOrWhiteSpace(found));

        return name is null ? "Linux" : name.Length > MaxBootEntryName ? name[..MaxBootEntryName] : name;
    }
}

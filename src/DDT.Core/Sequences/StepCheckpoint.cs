// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// What a step kept in this visit, and how it came to run again. SaveAsync persists at once, so a step can mark work
// started before it starts it.
public sealed class StepCheckpoint(string? saved, int restarts, bool interrupted, Func<string, Task> save)
{
    public string? Saved { get; private set; } = saved;

    public int Restarts { get; } = restarts;

    // The step was cut off: it runs again without having asked for a restart.
    public bool Interrupted { get; } = interrupted;

    // For a step run outside the engine. Nothing reaches a disk.
    public static StepCheckpoint Unsaved() => new(null, 0, false, _ => Task.CompletedTask);

    public async Task SaveAsync(string progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        await save(progress).ConfigureAwait(false);
        Saved = progress;
    }
}

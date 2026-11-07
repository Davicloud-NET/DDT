// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;

namespace DDT.Agent;

// The time the server has to answer the question about a run that was handed over to the installed Linux. It begins
// at a look before the update check, which comes first and takes from it, so a server that is away costs it once.
public sealed class HandedOverQuestionTime(TimeProvider timeProvider)
{
    private long? _began;

    // What is left of LinuxBootReturn.AskTime. All of it until a look found a run the question names.
    public TimeSpan Left => _began is { } began ? LinuxBootReturn.AskTime - timeProvider.GetElapsedTime(began) : LinuxBootReturn.AskTime;

    // The time the update check gets where the first seed holds a run the question names and gives a limit, else null
    // for a check that waits until the server answers. It writes no line: the look before the question does.
    public async Task<TimeSpan?> ForUpdateCheckAsync(IDisks disks, HandedOverSeeds seeds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(disks);
        ArgumentNullException.ThrowIfNull(seeds);

        IReadOnlyList<LocalDisk> listed;

        try
        {
            listed = await disks.ListDisksAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }

        if (seeds.Find(listed) is not [{ IsTaken: true, RunId: not null }, ..])
        {
            return null;
        }

        _began ??= timeProvider.GetTimestamp();

        return Left;
    }
}

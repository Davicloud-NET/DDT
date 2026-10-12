// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// What a save does to the disk, one call per step, so a test can pin their order.
internal interface IReplacementDisk
{
    // Writes a file that must not exist yet, through to the disk.
    Task WriteNewAsync(string path, byte[] content, CancellationToken cancellationToken);

    // Gives the file a second name. False where there is no file, or no second name to be had.
    bool Link(string path, string second);

    void Rename(string from, string to);

    // Has the folder's names written to the disk, which a rename does not wait for. False when that cannot be known.
    bool Flush(string directory);

    // Deletes what is at the path. With overwrite, a file of the agent's own loses its bytes first.
    void Remove(string path, bool overwrite);
}

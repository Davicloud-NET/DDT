// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.BootImage;

// Whether a stored sequence pre-provisions BitLocker, which takes manage-bde in the boot image.
public static class BootImageBitLocker
{
    private const string Kind = "\"preprovisionBitLocker\"";

    // A null definition is a sequence that doesn't exist.
    public static bool Needed(string? definition) =>
        definition is not null
        && definition.Contains(Kind, StringComparison.Ordinal)
        && SequenceTree.Leaves(SequenceDocuments.Read(definition)).OfType<PreprovisionBitLockerStep>().Any();

    // The text filter keeps the other sequences in the database.
    public static async Task<bool> NeededAsync(DdtDbContext database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        List<string> definitions = await database.TaskSequences
            .AsNoTracking()
            .Where(sequence => sequence.Definition.Contains(Kind))
            .Select(sequence => sequence.Definition)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return definitions.Any(Needed);
    }
}

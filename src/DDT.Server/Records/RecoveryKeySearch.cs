// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Records;

// Finds machines by a key ID as BitLocker's recovery screen shows it: the whole GUID, or its first 8 or more hex
// digits. The key IDs are compared here, because a GUID column can't be searched by its start in every database.
public sealed class RecoveryKeySearch(DdtDbContext database)
{
    public const int MinimumDigits = 8;

    public async Task<IReadOnlyList<Guid>> MachinesAsync(string? keyId, CancellationToken cancellationToken)
    {
        if (Digits(keyId) is not { } wanted)
        {
            return [];
        }

        var keys = await database.RecoveryKeys
            .AsNoTracking()
            .Select(k => new { k.MachineId, k.ProtectorId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. keys.Where(k => k.ProtectorId.ToString("N").StartsWith(wanted, StringComparison.Ordinal)).Select(k => k.MachineId).Distinct()];
    }

    // The hex digits in lower case, without braces and dashes. Null for anything else, or for too few.
    public static string? Digits(string? keyId)
    {
        string digits = (keyId ?? string.Empty).Trim().Trim('{', '}').Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

        return digits.Length is >= MinimumDigits and <= 32 && digits.All(char.IsAsciiHexDigit) ? digits : null;
    }
}

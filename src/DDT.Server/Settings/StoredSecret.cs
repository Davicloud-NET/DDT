// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Settings;

// Value is null when the secret was cleared or no longer decrypts, which Unreadable tells apart. Protected is the
// stored ciphertext, kept so a secret this process cannot read survives a save of the other fields.
public sealed record StoredSecret(string? Value, bool Unreadable, DateTimeOffset UpdatedUtc, string? Protected)
{
    // The generated ToString would print the secret into any log or assertion message that shows one.
    public override string ToString() => nameof(StoredSecret);
}

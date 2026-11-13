// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tests.Applications;

// How TestMsi lays out an MSI.
internal sealed record TestMsiOptions
{
    public IReadOnlyList<(string Name, string Value)> Properties { get; init; } = [];

    public int CodePage { get; init; } = 1252;

    // String references take 3 bytes, as Windows Installer writes them once a database has more than 65535 strings.
    public bool LongReferences { get; init; }

    // Empty string pool entries before the properties' strings. They push the ids up, and past 1023 of them the pool
    // is too large for the mini stream.
    public int EmptyStringsFirst { get; init; }

    public int SectorSize { get; init; } = 512;
}

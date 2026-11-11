// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tests.Applications;

// What TestExe puts into an installer EXE.
internal sealed record TestExeOptions
{
    // The StringFileInfo of the version resource; none at all when empty and FixedVersion is null.
    public IReadOnlyList<(string Key, string Value)> VersionStrings { get; init; } = [];

    public Version? FixedVersion { get; init; }

    // Bytes after the version resource in .rsrc, such as Inno Setup's offset table.
    public byte[] MoreResources { get; init; } = [];

    // The content of a .wixburn section, when there is one.
    public byte[]? BurnSection { get; init; }

    public byte[] Overlay { get; init; } = [];
}

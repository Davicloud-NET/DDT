// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Import;

// An entry of an MDT deployment share's Applications.xml, by its guid in {braces}. Name is cleaned to DDT's rules.
// Dependencies are guids of applications, bundles or nothing the share lists. Source and WorkingDirectory are as written.
public sealed record MdtShareApplication(
    string Id,
    string Name,
    string? Version,
    string? Publisher,
    string? Comments,
    string? CommandLine,
    string? WorkingDirectory,
    string? Source,
    string? UninstallKey,
    bool Reboot,
    IReadOnlyList<string> Dependencies)
{
    // MDT makes a bundle of an application without a command that depends on others.
    public bool IsBundle => string.IsNullOrWhiteSpace(CommandLine) && Dependencies.Count > 0;

    public bool HasFiles => !string.IsNullOrWhiteSpace(Source);
}

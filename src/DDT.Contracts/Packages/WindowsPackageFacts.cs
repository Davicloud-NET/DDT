// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Packages;

// Read from the file names only, but Builds, which the operator marks. Architecture is null unless every file names the
// same one.
public sealed record WindowsPackageFacts(
    WindowsPackageContent Content,
    IReadOnlyList<string> Languages,
    string? Architecture,
    IReadOnlyList<int> KbNumbers,
    IReadOnlyList<int> Builds);

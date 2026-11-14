// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.WindowsPackages;

// One .msu or .cab as its name describes it. Language is a tag such as de-DE, Architecture x64, arm64 or x86.
public sealed record WindowsPackageFile(string Name, WindowsPackageFileKind Kind, string? Language, string? Architecture, int? Kb);

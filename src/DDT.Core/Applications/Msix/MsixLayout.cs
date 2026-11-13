// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Applications.Msix;

// What DISM provisions: the main package, the framework packages it needs, and the Store licence at the files' root
// (a relative path) or null for none.
public sealed record MsixLayout(MsixPackageFile Main, IReadOnlyList<MsixPackageFile> Dependencies, string? License);

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Packages;

// What a Windows package holds, read from its file names: updates (.msu), a language pack with its features, features
// on demand, or updates and cabinets together.
public enum WindowsPackageContent
{
    Update,
    Language,
    Features,
    Mixed,
}

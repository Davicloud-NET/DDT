// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Import;

// What an MDT deployment share holds that DDT can import, and what it cannot.
public sealed record MdtShareView(
    string Path,
    IReadOnlyList<MdtImageFile> ImageFiles,
    IReadOnlyList<MdtDriverGroup> DriverGroups,
    MdtNotImported NotImported);

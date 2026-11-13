// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;

namespace DDT.Contracts.Import;

// An application or bundle of an MDT deployment share, by its guid. Name is what DDT would call it, and InLibrary says
// an application or bundle of that name is in the library already. Enabled is MDT's enable attribute.
public sealed record MdtApplication(
    string Id,
    string Name,
    string? Version,
    string? Publisher,
    bool HasFiles,
    ApplicationReferenceKind Kind,
    bool InLibrary,
    bool Enabled = true);

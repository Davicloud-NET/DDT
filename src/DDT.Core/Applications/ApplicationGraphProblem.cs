// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Applications;

// For a cycle, Path starts and ends with the same id. For a missing id, Path runs from the root to it.
public sealed record ApplicationGraphProblem(ApplicationGraphProblemKind Kind, IReadOnlyList<Guid> Path)
{
    // The application whose dependencies name the missing id, or null when a root is missing.
    public Guid? NamedBy => Kind == ApplicationGraphProblemKind.Missing && Path.Count > 1 ? Path[^2] : null;
}

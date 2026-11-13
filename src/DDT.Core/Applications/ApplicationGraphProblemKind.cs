// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Applications;

public enum ApplicationGraphProblemKind
{
    // Applications depend on each other in a circle.
    Cycle,

    // An id is not in the library.
    Missing,
}

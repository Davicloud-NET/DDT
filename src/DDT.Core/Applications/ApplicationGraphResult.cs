// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Applications;

// Either the install order or the problem, never both: Order is empty when there is a problem.
public sealed record ApplicationGraphResult(IReadOnlyList<Guid> Order, ApplicationGraphProblem? Problem);

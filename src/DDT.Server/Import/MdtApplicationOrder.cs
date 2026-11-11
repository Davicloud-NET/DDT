// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Import;

// The order applications are created in, and the dependencies left out because they close a circle.
public sealed record MdtApplicationOrder(
    IReadOnlyList<MdtShareApplication> Applications,
    IReadOnlySet<(string Application, string Dependency)> Cut);

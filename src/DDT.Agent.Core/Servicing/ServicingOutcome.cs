// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Servicing;

public enum ServicingOutcome
{
    Added,

    // DISM says the package does not fit this Windows, such as an update for another build.
    NotApplicable,
}

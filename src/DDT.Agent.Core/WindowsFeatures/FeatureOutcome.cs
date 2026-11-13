// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsFeatures;

public enum FeatureOutcome
{
    // Enabled now, or it was already.
    Enabled,

    // Enabled once the machine restarts.
    RestartNeeded,

    // DISM knows no feature of this name.
    Unknown,
}

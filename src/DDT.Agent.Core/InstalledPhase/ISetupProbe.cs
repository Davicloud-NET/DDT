// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.InstalledPhase;

public interface ISetupProbe
{
    // Null once the installed system's own setup has finished, in Windows including the out-of-box experience.
    // Otherwise it says what setup is still doing, for the log.
    string? Pending();
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

public interface IWindowsSetupProbe
{
    // Null once Windows setup has finished, the out-of-box experience included; otherwise what it is still doing, for
    // the log.
    string? Pending();
}

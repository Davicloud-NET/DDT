// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

public enum RestartTarget
{
    // Windows PE again, from the network.
    WindowsPE,

    // The system on the disk: the installed Windows, or the disk image the run wrote.
    InstalledSystem,
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Where a step runs: a boot environment, or the installed system after the hand-over.
public enum ConsolePhase
{
    WindowsPE,
    Windows,
    LinuxBoot,
    Linux,
}

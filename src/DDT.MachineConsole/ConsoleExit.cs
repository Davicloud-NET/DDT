// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole;

// The console's exit codes, which the agent reads.
public static class ConsoleExit
{
    public const int Closed = 0;
    public const int NoPipe = 2;
    public const int Refused = 3;
    public const int NotConnected = 4;
}

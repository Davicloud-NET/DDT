// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Consoles;

// Starts the graphical console, which connects to the named pipe pipeName.
public interface IConsoleLauncher
{
    IConsoleProcess Start(string pipeName);
}

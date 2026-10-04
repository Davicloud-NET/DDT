// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Host.Startup;

// The server an ADK install reads and changes. Tests stand in for it.
public interface IAdkMachine
{
    // KitsRoot10, the folder Windows kits install into
    string? KitsRoot();

    // As the ADK's setup registered it
    string? AdkVersion();

    // Windows runs one installer at a time, and the MSI starts the install from inside its own
    void WaitForWindowsInstaller();

    void Download(Uri address, string path);

    CommandResult Run(string program, IReadOnlyList<string> arguments);
}

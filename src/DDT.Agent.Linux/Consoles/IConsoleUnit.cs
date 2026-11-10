// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Consoles;

// The console's unit in the installed Linux.
internal interface IConsoleUnit
{
    // A virtual terminal nobody has open, or 0.
    int FreeTerminal();

    // Starts program on terminal, to connect to socket. Returns why systemd did not start it, or null.
    Task<string?> StartAsync(string program, string socket, int terminal, CancellationToken cancellationToken);

    // Stops a console that runs, also one an earlier agent started.
    Task StopAsync(CancellationToken cancellationToken);

    // Returns once the unit is gone, as after F9. False when cancellationToken ended the wait.
    Task<bool> WaitUntilGoneAsync(CancellationToken cancellationToken);
}

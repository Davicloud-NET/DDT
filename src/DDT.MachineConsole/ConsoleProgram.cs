// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;

namespace DDT.MachineConsole;

// The agent starts it with --pipe <name>. It connects before it opens a window. If no agent accepts it, it exits right
// away, so the agent's text console stays in view. With --session it's the shell of DDT's session.
public static class ConsoleProgram
{
    // Sets App.Startup and returns null when Avalonia may start; otherwise the exit code.
    public static int? Prepare(string[] args, ConsoleHost host)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(host);

        if (ConsolePipe.NameFrom(args) is not { } pipeName)
        {
            return ConsoleExit.NoPipe;
        }

        if (ConsolePipe.IsSession(args))
        {
            App.Startup = ConsoleStartup.ForSession(host, pipeName);

            return null;
        }

        ClientConnection connection;

        try
        {
            connection = ClientConnection.ConnectAsync(pipeName, $"DDT console {ConsoleBuild.Version}", CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (ConsoleProtocolException)
        {
            return ConsoleExit.Refused;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return ConsoleExit.NotConnected;
        }

        App.Startup = new ConsoleStartup(host, connection);

        return null;
    }
}

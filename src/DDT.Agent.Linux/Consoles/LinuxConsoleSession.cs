// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.InstalledPhase;
using DDT.Agent.LinuxPhase;
using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// The console in the installed Linux: the server's console as a unit on a terminal of its own, which reaches the agent
// through a socket. Nothing here fails the run. Without a picture it goes on, and the web shows it.
internal sealed class LinuxConsoleSession(
    SessionMachineConsole console,
    IAgentServer server,
    IConsoleUnit unit,
    LinuxAgentPaths paths,
    AgentLog log) : IDeploySession, IAsyncDisposable
{
    private const string Unseen = "so the run is not shown on this machine's screen.";

    private string? _socket;
    private bool _started;

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        // An earlier agent's console would wait for a socket that is gone.
        await unit.StopAsync(cancellationToken).ConfigureAwait(false);

        if (LinuxConsoleScreen.Problem(paths) is { } problem)
        {
            log.Information($"{problem}, {Unseen}");

            return;
        }

        if (await ConsoleUpdate.ServerConsoleAsync(server, log, paths.Program, cancellationToken).ConfigureAwait(false) is not { } program)
        {
            log.Information($"The server has no console for Linux, {Unseen}");

            return;
        }

        if (unit.FreeTerminal() is not (> 0 and int terminal))
        {
            log.Information($"No virtual terminal is free, {Unseen}");

            return;
        }

        string socket = Path.Combine(paths.ConsoleSockets, ConsolePipe.NewName());
        _socket = socket;
        console.Start(() => LinuxSessionPipe.Create(socket), ConsolePeer.Refusal);

        if (await unit.StartAsync(program, socket, terminal, cancellationToken).ConfigureAwait(false) is { } refusal)
        {
            log.Warning($"systemd did not start the console ({refusal}), {Unseen}");

            return;
        }

        _started = true;
        log.Information(string.Create(CultureInfo.InvariantCulture, $"The console shows the run on terminal {terminal}."));
    }

    public void SetupFinished()
    {
    }

    // After a failure the console shows how the run ended until someone at the machine presses F9.
    public async Task<bool> EndAsync(bool signOut, CancellationToken cancellationToken)
    {
        if (!_started)
        {
            return true;
        }

        if (!signOut)
        {
            log.Information("The console shows how the run ended until F9 is pressed at the machine.");

            return await unit.WaitUntilGoneAsync(cancellationToken).ConfigureAwait(false);
        }

        await unit.StopAsync(CancellationToken.None).ConfigureAwait(false);

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await console.DisposeAsync().ConfigureAwait(false);

        if (_socket is not null)
        {
            LinuxSessionPipe.Remove(_socket);
        }
    }
}

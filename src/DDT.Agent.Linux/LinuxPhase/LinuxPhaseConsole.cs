// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;

namespace DDT.Agent.LinuxPhase;

// The console on the machine's screen, which gets the run as in the boot environment. Without a unit to start it in,
// as in tests, it only collects what it would show.
internal sealed class LinuxPhaseConsole : IAsyncDisposable
{
    private readonly SessionMachineConsole _console;
    private readonly LinuxConsoleSession? _session;
    private readonly AgentLog _log;

    public LinuxPhaseConsole(LinuxPhaseMachine machine, IAgentServer server, AgentOptions options, AgentLog log, string version)
    {
        _log = log;
        _console = new SessionMachineConsole(log, version) { Name = "the console" };
        _session = machine.ConsoleUnit is { } unit ? new LinuxConsoleSession(_console, server, unit, machine.Paths, log) : null;
        Status = new ConsoleStatus(_console, AgentEnvironment.Linux, version, options.ServerUrl, options.KeyboardLayout, dryRun: false);
        Logo = new ConsoleLogo(server, Status, machine.Paths.Program, log);
        log.MachineConsole = _console;
    }

    public LinuxConsoleSession? Session => _session;

    public ConsoleStatus Status { get; }

    public ConsoleLogo Logo { get; }

    public async ValueTask DisposeAsync()
    {
        _log.MachineConsole = null;

        if (_session is not null)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            await _console.DisposeAsync().ConfigureAwait(false);
        }
    }
}

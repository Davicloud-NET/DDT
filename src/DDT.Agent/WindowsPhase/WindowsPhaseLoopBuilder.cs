// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;

namespace DDT.Agent.WindowsPhase;

// Puts a WindowsPhaseLoop together, the same way for the service, a dry run and the tests.
public sealed class WindowsPhaseLoopBuilder
{
    public required IAgentServer Server { get; init; }

    public required IMachineIdentityReader IdentityReader { get; init; }

    public required SequenceRunner Runner { get; init; }

    public required IWindowsSetupProbe Setup { get; init; }

    public required IRebooter Rebooter { get; init; }

    public required IRestartMarker RestartMarker { get; init; }

    public required IAgentRemoval Removal { get; init; }

    public required AgentLog Log { get; init; }

    public required TimeProvider TimeProvider { get; init; }

    public required TimeSpan HeartbeatInterval { get; init; }

    // The root of the installed Windows, such as C:\.
    public required string WindowsRoot { get; init; }

    public required string AgentVersion { get; init; }

    public required bool DryRun { get; init; }

    // DDT's session, where Windows shows the run on the console; Status is what that console shows.
    public IDeploySession? Session { get; init; }

    public ConsoleStatus? Status { get; init; }

    public ConsoleLogo? Logo { get; init; }

    public WindowsPhaseLoop Build()
    {
        WindowsPhaseOptions options = new(WindowsRoot, AgentVersion, DryRun);
        WindowsPhaseConsole console = new(Session, Status, Logo);
        WindowsRestart restart = new(Rebooter, RestartMarker, Log, TimeProvider, DryRun);

        return new WindowsPhaseLoop(
            new WindowsRunCalls(Server, IdentityReader, console, options, Log, TimeProvider),
            Runner,
            new WindowsSetupWait(Setup, new RunHeartbeatFactory(Server, Log, TimeProvider, HeartbeatInterval), console, Log, TimeProvider),
            new AgentExit(Removal, restart, console, Log),
            options,
            Log);
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;

namespace DDT.Agent.InstalledPhase;

// Puts an InstalledPhaseLoop together, the same way for the service, a dry run and the tests.
public sealed class InstalledPhaseLoopBuilder
{
    public required IAgentServer Server { get; init; }

    public required IMachineIdentityReader IdentityReader { get; init; }

    public required SequenceRunner Runner { get; init; }

    // The installed system the loop runs in.
    public required InstalledPhaseProfile Profile { get; init; }

    public required IRebooter Rebooter { get; init; }

    public required IRestartMarker RestartMarker { get; init; }

    public required IAgentRemoval Removal { get; init; }

    public required AgentLog Log { get; init; }

    public required TimeProvider TimeProvider { get; init; }

    public required TimeSpan HeartbeatInterval { get; init; }

    // The root of the installed system, such as C:\.
    public required string Root { get; init; }

    public required string AgentVersion { get; init; }

    public required bool DryRun { get; init; }

    // DDT's session, where the system shows the run on the console. Status is what that console shows.
    public IDeploySession? Session { get; init; }

    public ConsoleStatus? Status { get; init; }

    public ConsoleLogo? Logo { get; init; }

    public InstalledPhaseLoop Build()
    {
        InstalledPhaseOptions options = new(Profile, Root, AgentVersion, DryRun);
        InstalledPhaseConsole console = new(Session, Status, Logo);
        InstalledPhaseRestart restart = new(Rebooter, RestartMarker, Profile.Texts, Log, TimeProvider, DryRun);

        return new InstalledPhaseLoop(
            new InstalledPhaseCalls(Server, IdentityReader, console, options, Log, TimeProvider),
            Runner,
            new InstalledPhaseSetupWait(Profile, new RunHeartbeatFactory(Server, Log, TimeProvider, HeartbeatInterval), console, Log, TimeProvider),
            new InstalledPhaseExit(Removal, restart, console, Profile, new DeploymentRecordKeeper(Profile.DeploymentRecord, AgentVersion, Log, TimeProvider), Log),
            options,
            Log);
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.LinuxPhase;

// What the unit's agent reaches of the machine it runs on: where its files are, where its lines go and which file it
// is itself. A test gives it a folder for / and stands in for the rest, each part of which is the machine's when null.
internal sealed record LinuxPhaseMachine(LinuxAgentPaths Paths, TextWriter Output, string? ProcessPath)
{
    // The server the settings name.
    public Func<AgentOptions, IAgentServer>? Connect { get; init; }

    // The kernel's mount calls.
    public IMounts? Mounts { get; init; }

    // Takes what runs the agent's tools and returns what runs them instead, such as one that answers systemctl itself.
    public Func<IToolRunner, IToolRunner>? Tools { get; init; }

    // The machine's own identity, from its firmware and adapters.
    public IMachineIdentityReader? Identity { get; init; }

    // True once systemd has sent SIGTERM to the unit's processes to stop it. Null where nothing tells.
    public Func<bool>? UnitIsStopping { get; init; }

    // The machine the unit runs on.
    public static LinuxPhaseMachine Own() => new(LinuxAgentPaths.Machine, Console.Out, Environment.ProcessPath);
}

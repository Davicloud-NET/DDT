// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Contracts.Sequences;

// A sequence's kind, and the phases and the environments that belong to each kind.
public static class SequenceKinds
{
    // A sequence that writes a raw disk image on any branch is a Linux sequence.
    public static SequenceKind Of(SequenceDefinition definition) => Of(SequenceTree.Nodes(definition));

    public static SequenceKind Of(IEnumerable<SequenceStep> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        return nodes.Any(node => node is WriteRawImageStep) ? SequenceKind.Linux : SequenceKind.Windows;
    }

    // The kind whose sequences run in the phase.
    public static SequenceKind Of(SequencePhase phase) =>
        phase is SequencePhase.LinuxBoot or SequencePhase.Linux ? SequenceKind.Linux : SequenceKind.Windows;

    // The kind whose runs the environment takes part in.
    public static SequenceKind Of(AgentEnvironment environment) => Of(Phase(environment));

    // Where a run of the kind starts.
    public static SequencePhase BootPhase(SequenceKind kind) => kind == SequenceKind.Linux ? SequencePhase.LinuxBoot : SequencePhase.WindowsPE;

    public static SequencePhase BootPhase(SequenceDefinition definition) => BootPhase(Of(definition));

    // Where a run of the kind goes on after the hand-over.
    public static SequencePhase InstalledPhase(SequenceKind kind) => kind == SequenceKind.Linux ? SequencePhase.Linux : SequencePhase.Windows;

    public static bool IsBoot(SequencePhase phase) => phase is SequencePhase.WindowsPE or SequencePhase.LinuxBoot;

    // The environment an agent has to run in to start a run of the kind.
    public static AgentEnvironment BootEnvironment(SequenceKind kind) =>
        kind == SequenceKind.Linux ? AgentEnvironment.LinuxBoot : AgentEnvironment.WindowsPE;

    // The environment an agent runs in to go on with a run of the kind after the hand-over.
    public static AgentEnvironment InstalledEnvironment(SequenceKind kind) =>
        kind == SequenceKind.Linux ? AgentEnvironment.Linux : AgentEnvironment.Windows;

    // The phase an agent in the environment runs steps in. An environment this version does not know counts as Windows PE.
    public static SequencePhase Phase(AgentEnvironment environment) => environment switch
    {
        AgentEnvironment.Windows => SequencePhase.Windows,
        AgentEnvironment.LinuxBoot => SequencePhase.LinuxBoot,
        AgentEnvironment.Linux => SequencePhase.Linux,
        _ => SequencePhase.WindowsPE,
    };
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Server.Deployments;

// What an agent may do with a run, by the environment it registered from.
public static class PhaseRules
{
    // The sequence version from which an agent takes its resume token along into the other boot environment.
    public const int SwitchVersion = 4;

    // A boot environment reports its own phase and, around the hand-over, that of its installed system. An installed
    // system never takes a run back to the boot phase, and no environment reports the other kind's phases.
    public static bool MayReport(AgentEnvironment environment, SequencePhase phase) => environment switch
    {
        AgentEnvironment.WindowsPE => phase is SequencePhase.WindowsPE or SequencePhase.Windows,
        AgentEnvironment.Windows => phase == SequencePhase.Windows,
        AgentEnvironment.LinuxBoot => phase is SequencePhase.LinuxBoot or SequencePhase.Linux,
        AgentEnvironment.Linux => phase == SequencePhase.Linux,
        _ => false,
    };

    // Whether a run of the sequence starts in the environment. Only its kind's boot environment starts it.
    public static bool Starts(AgentEnvironment environment, SequenceDefinition definition) => StartsIn(definition) == environment;

    // The boot environment a sequence starts in.
    public static AgentEnvironment StartsIn(SequenceDefinition definition) => SequenceKinds.BootEnvironment(SequenceKinds.Of(definition));

    // The boot environment a machine in a boot environment can restart into. Null in an installed system.
    public static AgentEnvironment? Other(AgentEnvironment environment) => environment switch
    {
        AgentEnvironment.WindowsPE => AgentEnvironment.LinuxBoot,
        AgentEnvironment.LinuxBoot => AgentEnvironment.WindowsPE,
        _ => null,
    };

    // The environment in a sentence, as "registered from Windows PE".
    public static string Name(AgentEnvironment environment) => environment switch
    {
        AgentEnvironment.WindowsPE => "Windows PE",
        AgentEnvironment.LinuxBoot => "the Linux boot environment",
        AgentEnvironment.Linux => "the installed Linux",
        _ => "Windows",
    };

    // Why the agent's report is refused, or null when it may report the phase.
    public static string? ReportRefusal(AgentEnvironment environment, SequencePhase phase) =>
        MayReport(environment, phase)
            ? null
            : $"This machine registered from {Name(environment)}, so its run cannot be in the {PhaseName(phase)} phase. " +
              $"An agent in {Name(EnvironmentOf(phase))} registers before it reports.";

    // The environment whose agent runs the phase's steps.
    private static AgentEnvironment EnvironmentOf(SequencePhase phase) => SequenceKinds.IsBoot(phase)
        ? SequenceKinds.BootEnvironment(SequenceKinds.Of(phase))
        : SequenceKinds.InstalledEnvironment(SequenceKinds.Of(phase));

    private static string PhaseName(SequencePhase phase) => phase switch
    {
        SequencePhase.WindowsPE => "Windows PE",
        SequencePhase.LinuxBoot => "Linux boot environment",
        SequencePhase.Linux => "installed Linux",
        _ => "Windows",
    };
}

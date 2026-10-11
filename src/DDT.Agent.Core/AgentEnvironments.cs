// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Agent;

public static class AgentEnvironments
{
    // The environment in a sentence, as "this machine started Windows PE".
    public static string Name(AgentEnvironment environment) => environment switch
    {
        AgentEnvironment.WindowsPE => "Windows PE",
        AgentEnvironment.LinuxBoot => "the Linux boot environment",
        AgentEnvironment.Linux => "the installed Linux",
        _ => "the installed Windows",
    };

    // The boot environment that runs the boot phase, in a sentence.
    public static string OfBootPhase(SequencePhase phase) => Name(SequenceKinds.BootEnvironment(SequenceKinds.Of(phase)));
}

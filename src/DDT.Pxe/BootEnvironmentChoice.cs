// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Pxe;

// The rule that picks a machine's boot environment from its wish, the default, and what the server can start.
public static class BootEnvironmentChoice
{
    // Null means neither can be started for this machine, so it gets no answer.
    public static AgentEnvironment? Decide(BootWish wish, DefaultBootEnvironment preferred, bool windowsPE, bool linux)
    {
        ArgumentNullException.ThrowIfNull(wish);

        bool linuxStarts = linux && !wish.LinuxRefused;

        // A wish that cannot be met starts the other environment, whose agent says why the run does not start.
        if (wish.Wanted == AgentEnvironment.LinuxBoot)
        {
            return linuxStarts ? AgentEnvironment.LinuxBoot : windowsPE ? AgentEnvironment.WindowsPE : null;
        }

        if (wish.Wanted == AgentEnvironment.WindowsPE)
        {
            return windowsPE ? AgentEnvironment.WindowsPE : linuxStarts ? AgentEnvironment.LinuxBoot : null;
        }

        // Without a wish, a machine is always named the Windows PE boot file when nothing else fits, as before.
        return preferred switch
        {
            DefaultBootEnvironment.Linux when linuxStarts => AgentEnvironment.LinuxBoot,
            DefaultBootEnvironment.Auto when !windowsPE && linuxStarts => AgentEnvironment.LinuxBoot,
            _ => AgentEnvironment.WindowsPE,
        };
    }
}

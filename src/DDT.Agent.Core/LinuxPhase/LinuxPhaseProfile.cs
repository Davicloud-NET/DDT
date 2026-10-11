// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Agent.LinuxPhase;

// The installed Linux as the installed phase's loop sees it.
public static class LinuxPhaseProfile
{
    // cloud-init finishes within a second of the unit's start, and the settle time is counted from when that is seen.
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    // setup says what cloud-init still does, which a dry run and the tests fake. Linux deletes a running program's
    // file, so nothing is left for one more restart to remove.
    public static InstalledPhaseProfile With(ISetupProbe setup) => new()
    {
        Environment = AgentEnvironment.Linux,
        Phase = SequencePhase.Linux,
        Setup = setup,
        WaitActivity = RunActivity.WaitingForCloudInit,
        SetupPollInterval = PollInterval,
        RestartsAfterRemoval = false,
        Texts = new LinuxPhaseTexts(),
    };
}

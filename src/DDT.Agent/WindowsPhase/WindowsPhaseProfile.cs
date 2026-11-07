// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Agent.WindowsPhase;

// The installed Windows as the installed phase's loop sees it.
public static class WindowsPhaseProfile
{
    // setup says what Windows setup still does, which a dry run and the tests fake.
    public static InstalledPhaseProfile With(ISetupProbe setup) => new()
    {
        Environment = AgentEnvironment.Windows,
        Phase = SequencePhase.Windows,
        Setup = setup,
        WaitActivity = RunActivity.WaitingForWindowsSetup,
        RestartsAfterRemoval = true,
        Texts = new WindowsPhaseTexts(),
    };
}

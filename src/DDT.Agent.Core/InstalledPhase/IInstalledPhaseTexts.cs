// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.InstalledPhase;

// The sentences of the installed phase's log that name its system, each as that system says it.
public interface IInstalledPhaseTexts
{
    // The error of a run whose state is gone.
    string StateGone { get; }

    // The error of a run whose kept last report cannot be read.
    string FinalReportLost { get; }

    string SetupFinished { get; }

    string RestartWasDue { get; }

    string StoppedAfterRemoval { get; }

    string RestartsOnceMore { get; }

    string Started(string agentVersion);

    string StillInBootEnvironment(Guid runId);

    string WaitingForSetup(string pending);

    string ReportRefusedDuringSetup(string failure);

    string SetupTakesLong(string waited, string pending);

    string CannotRestart(string failure);

    string NotRestarted(TimeSpan after);

    string CannotRestartOnceMore(string failure);
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;
using DDT.Agent.Sequences;

namespace DDT.Agent.WindowsPhase;

// What the installed phase's log says in the installed Windows.
public sealed class WindowsPhaseTexts : IInstalledPhaseTexts
{
    public const string StateGoneMessage = "The run's state in the installed Windows is gone, so the run cannot go on there.";

    public const string FinalReportLostMessage = "The run ended in the installed Windows, but how it ended cannot be read from the disk there.";

    public string StateGone => StateGoneMessage;

    public string FinalReportLost => FinalReportLostMessage;

    public string SetupFinished => "Windows setup has finished.";

    public string RestartWasDue => "Windows was to restart for the run but has not restarted since. Restarting it now.";

    public string StoppedAfterRemoval => "The agent was stopped. What is left of it goes when Windows next starts.";

    public string RestartsOnceMore => "Windows restarts once more, which deletes what is left of the agent.";

    public string Started(string agentVersion) =>
        $"DDT agent {agentVersion}, the {OfflineServiceRegistration.ServiceName} service of the installed Windows.";

    public string StillInBootEnvironment(Guid runId) =>
        $"Run {runId} still goes on in Windows PE, which hands it over again. The agent leaves it alone.";

    public string WaitingForSetup(string pending) =>
        $"Waiting for Windows setup to finish: {pending}. The run goes on once it has, however long that takes.";

    public string ReportRefusedDuringSetup(string failure) =>
        $"The server did not take the report while Windows setup runs ({failure}). Registering again.";

    public string SetupTakesLong(string waited, string pending) =>
        $"Windows setup has not finished after {waited}: {pending}. If it waits for someone at the machine, finish it there.";

    public string CannotRestart(string failure) =>
        $"Windows could not restart itself ({failure}). Restart it by hand; the run goes on after the restart.";

    public string NotRestarted(TimeSpan after) =>
        $"Windows has not restarted {after.TotalMinutes:0} minutes after the run asked it to. Asking again.";

    public string CannotRestartOnceMore(string failure) =>
        $"Windows could not restart itself ({failure}). What is left of the agent goes when Windows next starts.";
}

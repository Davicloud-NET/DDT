// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;
using DDT.Core.CloudInit;

namespace DDT.Agent.LinuxPhase;

// What the installed phase's log says in the installed Linux.
public sealed class LinuxPhaseTexts : IInstalledPhaseTexts
{
    public const string StateGoneMessage = "The run's state in the installed Linux is gone, so the run cannot go on there.";

    public const string FinalReportLostMessage = "The run ended in the installed Linux, but how it ended cannot be read from the disk there.";

    public string StateGone => StateGoneMessage;

    public string FinalReportLost => FinalReportLostMessage;

    public string SetupFinished => "The wait for cloud-init is over, and the run goes on.";

    public string RestartWasDue => "Linux was to restart for the run but has not restarted since. Restarting it now.";

    public string StoppedAfterRemoval => "The agent was stopped after it removed itself.";

    public string RestartsOnceMore => "Linux restarts once more.";

    public string Started(string agentVersion) =>
        $"DDT agent {agentVersion}, the {LinuxAgentFiles.UnitName} unit of the installed Linux.";

    public string StillInBootEnvironment(Guid runId) =>
        $"Run {runId} still goes on in the Linux boot environment, which hands it over again. The agent leaves it alone.";

    // At a later start the wait is for what cloud-init does at every start, which the pending text then says itself.
    public string WaitingForSetup(string pending) => pending == CloudInitWait.RunsAgain
        ? $"Waiting for cloud-init: {pending}. The run goes on after it."
        : $"Waiting for cloud-init, which sets this Linux up at its first start: {pending}. The run goes on after it.";

    public string ReportRefusedDuringSetup(string failure) =>
        $"The server did not take the report while the run waits for cloud-init ({failure}). Registering again.";

    public string SetupTakesLong(string waited, string pending) =>
        $"The run has waited {waited} for cloud-init: {pending}. cloud-init status --long says on the machine what it does.";

    public string CannotRestart(string failure) =>
        $"Linux could not restart itself ({failure}). Restart it by hand; the run goes on after the restart.";

    public string NotRestarted(TimeSpan after) =>
        $"Linux has not restarted {after.TotalMinutes:0} minutes after the run asked it to. Asking again.";

    public string CannotRestartOnceMore(string failure) =>
        $"Linux could not restart itself ({failure}).";
}

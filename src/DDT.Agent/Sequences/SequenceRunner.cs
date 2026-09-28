// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// Runs a task sequence in WinPE, or continues it in the installed Windows, and resumes a run found on disk. It never
// throws. It reports every failure, because if the agent crashes, the one from the boot image takes over.
public sealed class SequenceRunner
{
    public const string NoDiskMessage =
        "No internal disk was found. If this PC's storage is set to RAID or Intel VMD/RST, switch it to AHCI in the " +
        "firmware setup: DDT's boot image has no driver for it.";

    public const string SeveralDisksMessage =
        "This machine has more than one disk DDT could install on. Restart it from the network and sign in at it to " +
        "choose the disk.";

    public const string LostContactMessage = "The agent lost contact with the server during the run.";

    public const string WindowsDidNotStartMessage =
        "The machine keeps starting Windows PE instead of the installed Windows, so the run cannot go on there. Set its " +
        "firmware to start Windows Boot Manager first, then run the sequence again.";

    public const string WindowsPEAfterWindowsMessage =
        "A step that runs in Windows PE follows the steps in the installed Windows, and the run cannot go back to Windows PE. " +
        "Move the step before the first step in Windows, then run the sequence again.";

    private readonly SequenceRunFactory _runs;
    private readonly RunStart _start;
    private readonly PhaseRunner _phases;
    private readonly RunEnding _ending;
    private readonly SequenceRunnerOptions _options;
    private readonly AgentLog _log;

    // Internal, like its parts. SequenceRunnerBuilder puts it together.
    internal SequenceRunner(
        SequenceRunFactory runs,
        RunStart start,
        PhaseRunner phases,
        RunEnding ending,
        SequenceRunnerOptions options,
        AgentLog log)
    {
        _runs = runs;
        _start = start;
        _phases = phases;
        _ending = ending;
        _options = options;
        _log = log;
    }

    public Task<RunResult> RunAsync(RunRequest request, CancellationToken cancellationToken) =>
        RunAsync(SequencePhase.WindowsPE, _ending.RecordWindowsPERestart, request, cancellationToken);

    // If WinPE recorded a pending restart, it happens first at the next start. Returns how it ended, or null if none
    // was due.
    public Task<RunOutcome?> RestartIfDueAsync(CancellationToken cancellationToken) => _ending.RestartIfDueAsync(cancellationToken);

    // request.Resumed is the run as the hand-over left it in the running Windows. The run ends with its Done report and
    // no restart, because the agent still has to remove itself. recordRestart keeps a due restart in case the service
    // stops or dies before it happens.
    public Task<RunResult> GoOnInWindowsAsync(RunRequest request, Action recordRestart, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Resumed);
        ArgumentNullException.ThrowIfNull(recordRestart);

        return RunAsync(SequencePhase.Windows, recordRestart, request, cancellationToken);
    }

    private async Task<RunResult> RunAsync(SequencePhase phase, Action recordRestart, RunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        SequenceRun run = _runs.Open(phase, recordRestart, request);
        RunResult result = await _start.StartAsync(run, cancellationToken).ConfigureAwait(false)
            ?? await _phases.RunAsync(run, cancellationToken).ConfigureAwait(false);

        // In Windows the agent's removal deletes the dry run's root.
        if (_options.DryRun && phase == SequencePhase.WindowsPE && result.Outcome is RunOutcome.Finished or RunOutcome.Failed)
        {
            Leftovers.Delete(_options.WorkDirectory, _log);
        }

        return result;
    }
}

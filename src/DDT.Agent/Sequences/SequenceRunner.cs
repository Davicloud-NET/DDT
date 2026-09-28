// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Runs a task sequence in Windows PE. A fresh run is checked first, so nothing is erased for a run that cannot
// succeed; a run found on the disk after a restart goes on where it was, or is handed over again when the installed
// Windows was to go on with it. The engine runs the steps, and the Windows PE phase ends in one of three ways: a
// restart back into Windows PE for the rest of the run, the hand-over to the installed Windows for its steps there, or
// the end of the run, after which the machine restarts into the Windows it installed. Nothing it does may end the
// agent: every failure is reported, because an agent that crashes is replaced by the boot image's. In a dry run,
// workDirectory is the dry run's root, which stands in for the disk and is deleted when the run ends. systemDirectory
// is where Windows PE keeps its tools.
// restartMarker keeps a restart that leaves Windows PE from the moment it is due until the restart: a restart the engine
// asks for as soon as it does, as the state already goes on after the step, and a restart into the installed Windows
// for as long as this run keeps Windows Boot Manager first. An agent that stopped short of such a restart, or whose
// restart failed, makes it at its next start through RestartIfDueAsync.
// The service in the installed Windows goes on with the run through GoOnInWindowsAsync, with the same steps, reports
// and failure handling. The engine never runs a Windows PE step there, as the phases come in order, so the disk, image
// and boot tools and the restart marker are only there for Windows PE. status, in Windows PE, is what the console at the
// machine shows, which the run keeps up to date; nobody watches the service's. accountTools connects the shares of a
// step and signs in the account a script runs as; without it, a dry run only logs them and any other run uses Windows.
public sealed class SequenceRunner(
    IAgentServer server,
    IDiskPartitioner partitioner,
    IRawDisks rawDisks,
    IImageApplier applier,
    IBcdWriter bcdWriter,
    IRebooter rebooter,
    WindowsPERestartMarker restartMarker,
    IToolRunner tools,
    IDomainJoiner joiner,
    WindowsHandOver handOver,
    AgentLog log,
    TimeProvider timeProvider,
    TimeSpan heartbeatInterval,
    string workDirectory,
    string systemDirectory,
    bool dryRun,
    ConsoleStatus? status = null,
    AccountTools? accountTools = null)
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

    // 2 GB more keeps the downloads and the applied image from filling the disk to the last byte.
    private const long SpareBytes = 2048L * 1024 * 1024;
    private const long Megabyte = 1024L * 1024;

    private const int MaxFinalFlushes = 20;
    private const int MaxFinalFlushFailures = 3;

    // How often Windows PE may start instead of the installed Windows and hand the run over again.
    private const int MaxWindowsPEReturns = 3;

    // Firmware setup screens show a line of this much.
    private const int MaxBootEntryName = 64;

    // Whether this run put Windows Boot Manager first, which a run that does not finish puts back.
    private bool _windowsFirst;

    // Where the run goes on: in Windows PE, or in the installed Windows.
    private SequencePhase _phase;

    // Records that the run restarts the machine back to where it runs now.
    private Action _recordRestart = () => { };

    // confirmedDisk is the disk the technician confirmed with ERASE in this process, if any. Disk numbers can change
    // when the machine starts again, so a run chosen at the machine only erases that same disk. resumed is the run's
    // state found on the disk, for a run that goes on after a restart.
    public Task<RunResult> RunAsync(
        Guid machineId,
        AgentRun run,
        LocalRun? resumed,
        LocalDisk? confirmedDisk,
        DeploymentTokens tokens,
        MachineIdentity identity,
        CancellationToken cancellationToken) =>
        RunAsync(
            SequencePhase.WindowsPE,
            () => restartMarker.Set(RestartInto.WindowsPE),
            machineId,
            run,
            resumed,
            confirmedDisk,
            tokens,
            identity,
            cancellationToken);

    // A restart that Windows PE recorded but that did not happen, as the agent was stopped before it or wpeutil failed,
    // comes before anything else at the next start, the same way: the run's state already goes on after it. Returns
    // how the restart ended, or null when none is due. A start that was stopped already, for example with Ctrl+C
    // during the update check, leaves it due.
    public async Task<RunOutcome?> RestartIfDueAsync(CancellationToken cancellationToken)
    {
        if (restartMarker.Due is not { } into)
        {
            return null;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return RunOutcome.Stopped;
        }

        log.Warning(into == RestartInto.WindowsPE
            ? "The machine was to restart into Windows PE for the run but has not restarted since. Restarting it now."
            : "The machine was to start the installed Windows but has not restarted since. Restarting it now.");

        RunResult result = await RebootAsync(into, RestartReason.WasDue, RunOutcome.Restarting, "Restart it by hand.", cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome;
    }

    // resumed is the run as the hand-over left it in the running Windows, whose volume it names. The run ends with its
    // Done report and no restart, as the agent still has to remove itself. recordRestart is called as soon as the run
    // knows that it restarts Windows, before it tells the server, so an agent that stops or dies before the restart
    // finds out at its next start that the restart is still due.
    public Task<RunResult> GoOnInWindowsAsync(
        Guid machineId,
        AgentRun run,
        LocalRun resumed,
        DeploymentTokens tokens,
        MachineIdentity identity,
        Action recordRestart,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resumed);
        ArgumentNullException.ThrowIfNull(recordRestart);

        return RunAsync(SequencePhase.Windows, recordRestart, machineId, run, resumed, null, tokens, identity, cancellationToken);
    }

    private async Task<RunResult> RunAsync(
        SequencePhase phase,
        Action recordRestart,
        Guid machineId,
        AgentRun run,
        LocalRun? resumed,
        LocalDisk? confirmedDisk,
        DeploymentTokens tokens,
        MachineIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(identity);

        _windowsFirst = false;
        _phase = phase;
        _recordRestart = recordRestart;

        RunSession session = new(machineId, run, tokens)
        {
            SecureBootEnabled = identity.SecureBootEnabled,
            TrustedUefiCas = identity.TrustedUefiCas,
        };
        SequenceState state = resumed?.State ?? SequenceStates.Start(run.Id, run.Sequence);
        MachineVariables machine = RunMachine.Of(identity, run);

        FileRunStateStore? store = null;
        RunHeartbeat heartbeat = new(
            server,
            log,
            tokens,
            machineId,
            run.Id,
            call => store?.SaveTokenAsync(call) ?? Task.CompletedTask,
            heartbeatInterval,
            timeProvider);
        StepStateLog stepStates = new(log, state, machine);
        store = new FileRunStateStore(
            tokens,
            saved =>
            {
                stepStates.Saved(saved);
                heartbeat.Update(saved);
            });

        if (status is not null)
        {
            status.RunBegins(run, state);
            heartbeat.Changed += () => status.RunChanged(heartbeat);
        }

        heartbeat.Update(state);

        RunResult result = await RunCoreAsync(session, resumed, confirmedDisk, store, heartbeat, state, machine, cancellationToken)
            .ConfigureAwait(false);

        // In Windows the agent's removal deletes the dry run's root.
        if (dryRun && phase == SequencePhase.WindowsPE && result.Outcome is RunOutcome.Finished or RunOutcome.Failed)
        {
            Leftovers.Delete(workDirectory, log);
        }

        return result;
    }

    private async Task<RunResult> RunCoreAsync(
        RunSession session,
        LocalRun? resumed,
        LocalDisk? confirmedDisk,
        FileRunStateStore store,
        RunHeartbeat heartbeat,
        SequenceState state,
        MachineVariables machine,
        CancellationToken cancellationToken)
    {
        AgentRun run = session.Run;

        // A tree counts the steps on all its branches, as they are before the run takes any of them.
        int count = SequenceTree.Leaves(run.Sequence).Count;

        try
        {
            if (resumed is null)
            {
                log.Information($"The run of {run.SequenceName} begins: {(count == 1 ? "1 step" : $"{count} steps")}.");
                await PreflightAsync(session, confirmedDisk, machine, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                log.Information($"The run of {run.SequenceName} goes on {WhereItGoesOn(state, count)}.");
                await ResumeAsync(session, resumed, store, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (AgentTokenRejectedException)
        {
            log.Warning("The server no longer accepts this machine's token. Registering again; nothing was changed on any disk.");

            return new RunResult(RunOutcome.TokenRejected);
        }
        catch (Exception exception)
        {
            string message = LogText.OneLine(exception);
            log.Error(resumed is null ? $"The run cannot start: {message}" : $"The run cannot go on: {message}");
            status?.RunFailed(message);
            AgentRunReport report = heartbeat.Snapshot(DeploymentState.Failed, message);

            if (resumed is not null)
            {
                await EndRunAsync(resumed.Files, report).ConfigureAwait(false);
                LocalRun.DeleteAnswerFile(resumed.State, resumed.WindowsRoot, log);
            }

            return await ReportFailedAsync(heartbeat, resumed?.Files, report, cancellationToken).ConfigureAwait(false);
        }

        // Nothing is changed on any disk before the server has the run as running. A run that goes on is running
        // already, and the heartbeat's first beat tells the server where it is. A run that waits for answers to its
        // inputs says so from its first report.
        bool waitsForInputs = resumed is null && run.PendingInputs is { Count: > 0 };

        try
        {
            if (resumed is null)
            {
                if (waitsForInputs)
                {
                    heartbeat.Activity = RunActivity.WaitingForInput;
                }

                await ServerCallRules.CallAsync(
                    call => heartbeat.ReportAsync(heartbeat.Snapshot(DeploymentState.Running), call),
                    "the start of the run",
                    log,
                    timeProvider,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (AgentTokenRejectedException)
        {
            log.Warning("The server no longer accepts this machine's token. Registering again; nothing was changed on any disk.");

            return new RunResult(RunOutcome.TokenRejected);
        }
        catch (Exception exception)
        {
            string message = $"The server did not let the run start: {LogText.OneLine(exception)} Nothing was changed on any disk.";
            log.Error(message);
            status?.RunFailed(message);

            return new RunResult(RunOutcome.Failed);
        }

        return await RunStepsAsync(session, store, heartbeat, state, machine, waitsForInputs, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RunResult> RunStepsAsync(
        RunSession session,
        FileRunStateStore store,
        RunHeartbeat heartbeat,
        SequenceState state,
        MachineVariables machine,
        bool waitsForInputs,
        CancellationToken cancellationToken)
    {
        SequenceEngine engine = new(Steps(session, store, heartbeat), store, heartbeat);
        SequenceOutcome outcome = SequenceOutcome.Failed;
        string? error = null;

        // The engine saved the step that asked for a restart as done, so a resume goes on after it: once that is on the
        // disk, only the restart itself keeps the next steps from running without it.
        bool restartDue = false;

        using CancellationTokenSource steps = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        heartbeat.Activity = waitsForInputs ? RunActivity.WaitingForInput : RunActivity.Step;
        heartbeat.Start(steps, cancellationToken);

        try
        {
            // The values the run starts with come once its inputs are answered, and conditions and scripts read them. A run
            // this agent got before it started has none of its own: they came with the report that started it.
            if (waitsForInputs)
            {
                machine = machine with { Variables = await WaitForInputsAsync(session, heartbeat, steps.Token).ConfigureAwait(false) };
                heartbeat.Activity = RunActivity.Step;
            }
            else if (machine.Variables is null && heartbeat.Values is { } started)
            {
                machine = machine with { Variables = started };
            }

            SequenceRunResult result = state.Phase == SequencePhase.Windows && _phase == SequencePhase.WindowsPE
                ? await HandOverAgainAsync(store, state).ConfigureAwait(false)
                : await engine.RunAsync(state, machine, steps.Token).ConfigureAwait(false);
            state = result.State;
            outcome = result.Outcome;
            error = result.Error;
            restartDue = outcome == SequenceOutcome.RebootRequired;

            if (restartDue)
            {
                _recordRestart();
            }

            switch (outcome)
            {
                case SequenceOutcome.Completed when _phase == SequencePhase.WindowsPE && WindowsApplied(state):
                    heartbeat.Activity = RunActivity.Finishing;
                    await MakeBootableAsync(session.RequireVolumes(), null, steps.Token).ConfigureAwait(false);
                    break;
                case SequenceOutcome.Completed when _phase == SequencePhase.WindowsPE && RawImageWritten(state):
                    heartbeat.Activity = RunActivity.Finishing;
                    await PutRawImageFirstAsync(session, state, steps.Token).ConfigureAwait(false);
                    break;
                case SequenceOutcome.Completed:
                    heartbeat.Activity = RunActivity.Finishing;
                    break;
                case SequenceOutcome.PhaseChangeRequired when _phase == SequencePhase.Windows:
                    throw new DeploymentStepException(WindowsPEAfterWindowsMessage);
                case SequenceOutcome.PhaseChangeRequired:
                    heartbeat.Activity = RunActivity.HandingOver;
                    SequenceState handedOver = state;
                    await MakeBootableAsync(
                        session.RequireVolumes(),
                        () => handOver.StageAsync(session, store, handedOver, steps.Token),
                        steps.Token).ConfigureAwait(false);
                    break;
                case SequenceOutcome.RebootRequired:
                    heartbeat.Activity = RunActivity.Restarting;
                    await store.SaveTokenAsync(CancellationToken.None).ConfigureAwait(false);
                    break;
            }

            // A beat still on its way can be refused, for example because an operator stopped the run. That still
            // ends the run, before the last reports.
            await heartbeat.StopAsync().ConfigureAwait(false);
            steps.Token.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            await heartbeat.StopAsync().ConfigureAwait(false);
            outcome = SequenceOutcome.Failed;
            error = LogText.OneLine(exception);
            state = store.State ?? state;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await RestoreBootOrderAsync().ConfigureAwait(false);
            log.Warning("The agent was stopped. The run's state stays on the disk, so the run goes on after a restart if the server still runs it.");

            return new RunResult(RunOutcome.Stopped);
        }

        if (heartbeat.Failure is AgentTokenRejectedException && restartDue)
        {
            log.Warning("The server no longer accepts this machine's token. The machine restarts as the run asked, and the run goes on after the restart if the server still runs it.");

            return await RebootAsync(
                SamePhase,
                RestartReason.StepAsked,
                RunOutcome.Restarting,
                "Restart it by hand; the run goes on after the restart.",
                cancellationToken).ConfigureAwait(false);
        }

        if (heartbeat.Failure is AgentTokenRejectedException)
        {
            return await TokenRejectedAsync(heartbeat).ConfigureAwait(false);
        }

        if (heartbeat.Failure is { } refusal)
        {
            return await FailAsync(session, store, heartbeat, state, LogText.OneLine(refusal), cancellationToken).ConfigureAwait(false);
        }

        return outcome switch
        {
            SequenceOutcome.Completed => await FinishAsync(session, store, heartbeat, state, cancellationToken).ConfigureAwait(false),
            SequenceOutcome.RebootRequired => await RestartAsync(heartbeat, SamePhase, cancellationToken).ConfigureAwait(false),
            SequenceOutcome.PhaseChangeRequired => await RestartAsync(heartbeat, RestartInto.Windows, cancellationToken).ConfigureAwait(false),
            SequenceOutcome.Stopped => new RunResult(RunOutcome.Stopped),
            _ => await FailAsync(session, store, heartbeat, state, error ?? "The run failed.", cancellationToken).ConfigureAwait(false),
        };
    }

    // Windows PE started, but the run goes on in the installed Windows: the hand-over was interrupted, or the firmware
    // started the network first. Doing the hand-over again repeats nothing that could harm what it did before.
    private async Task<SequenceRunResult> HandOverAgainAsync(FileRunStateStore store, SequenceState state)
    {
        int returns = int.TryParse(state.Variables.GetValueOrDefault(RunVariables.WindowsPEReturns), NumberStyles.None, CultureInfo.InvariantCulture, out int earlier)
            ? earlier + 1
            : 1;

        if (returns > MaxWindowsPEReturns)
        {
            return new SequenceRunResult(SequenceOutcome.Failed, state, WindowsDidNotStartMessage);
        }

        log.Warning($"The run goes on in the installed Windows, but the machine started Windows PE. Handing the run over again ({returns} of {MaxWindowsPEReturns} times).");

        Dictionary<string, string> variables = new(state.Variables, StringComparer.Ordinal)
        {
            [RunVariables.WindowsPEReturns] = returns.ToString(CultureInfo.InvariantCulture),
        };
        state = state with { Phase = SequencePhase.WindowsPE, Variables = variables };
        await store.SaveAsync(state, CancellationToken.None).ConfigureAwait(false);

        return new SequenceRunResult(SequenceOutcome.PhaseChangeRequired, state, null);
    }

    // The run waits at its start for the answers to its inputs, which the person at the machine gives here and someone on
    // the web on the machine's page. Either way the run's values come from the server once nothing is pending: in the
    // answer to the answers sent from here, or in a report's answer, which also takes the question here away. A question
    // the server did not take is asked again with what was wrong. No answer is ever logged, and an Account input's goes to
    // the server alone.
    private async Task<IReadOnlyDictionary<string, string>> WaitForInputsAsync(RunSession session, RunHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        AgentRun run = session.Run;
        IReadOnlyList<AgentInput> pending = [.. run.PendingInputs ?? []];
        IReadOnlyDictionary<string, string> errors = new Dictionary<string, string>();
        string? error = null;
        Task<IReadOnlyDictionary<string, string>> fromWeb = heartbeat.WaitForValuesAsync(cancellationToken);
        IMachineConsole? console = status?.Console;

        log.Information($"The run waits for answers to {string.Join(", ", pending.Select(input => input.Label))}, at this machine or on the machine's page.");

        while (!fromWeb.IsCompleted && pending.Count > 0 && console is not null && (console.CanAsk || console is SessionMachineConsole))
        {
            using CancellationTokenSource asking = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            InputsQuestion question = new(
                run.SequenceName,
                [.. pending.Select(input => InputQuestions.ToConsole(input, errors.GetValueOrDefault(input.Name), run.Sequence))],
                error);
            Task<ConsoleAnswer?> asked = console.AskAsync(question, asking.Token);

            if (await Task.WhenAny(asked, fromWeb).ConfigureAwait(false) == fromWeb)
            {
                await asking.CancelAsync().ConfigureAwait(false);
                await ((Task)asked).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                break;
            }

            // Nobody can answer here after all: the web decides.
            if (await asked.ConfigureAwait(false) is not { } answer)
            {
                break;
            }

            if (answer.Values is not { } given)
            {
                continue;
            }

            errors = InputQuestions.Check(pending, given);
            error = null;

            if (errors.Count > 0)
            {
                log.Warning($"Answer these again: {string.Join(", ", pending.Where(input => errors.ContainsKey(input.Name)).Select(input => input.Label))}.");

                continue;
            }

            try
            {
                AgentAnswersResult result = await ServerCallRules.CallAsync(
                    call => server.AnswerRunInputsAsync(session.MachineId, session.Tokens.Token, run.Id, new AgentInputAnswers(InputQuestions.Answers(pending, given)), call),
                    "the answers",
                    log,
                    timeProvider,
                    cancellationToken).ConfigureAwait(false);

                if (result.Values is { } started)
                {
                    log.Information("The server took the answers, and the run starts.");

                    return started;
                }

                pending = [.. result.InputsPending ?? []];
                errors = (result.Problems ?? []).Where(problem => problem is not null).GroupBy(problem => problem.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(problems => problems.Key, problems => problems.First().Message, StringComparer.OrdinalIgnoreCase);

                if (errors.Count > 0)
                {
                    log.Warning($"The server did not take the answers to {string.Join(", ", errors.Keys)}.");
                }
            }
            catch (AgentTokenRejectedException exception)
            {
                heartbeat.TokenRejected(exception);

                throw;
            }
            catch (DeploymentStepException exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A refusal names the answers it did not take where it can; either way the question comes again.
                error = exception.Message;
                errors = InputQuestions.FieldErrors(pending, (exception.InnerException as AgentRequestException)?.FieldErrors) ?? new Dictionary<string, string>();
                log.Warning($"The answers were not taken: {exception.Message}");
            }
        }

        IReadOnlyDictionary<string, string> values = await fromWeb.ConfigureAwait(false);
        log.Information("The inputs were answered on the web, and the run starts.");

        return values;
    }

    // A restart the run asks for leads back to where it runs now.
    private RestartInto SamePhase => _phase == SequencePhase.Windows ? RestartInto.Windows : RestartInto.WindowsPE;

    private static bool WindowsApplied(SequenceState state) =>
        state.Variables.TryGetValue(RunVariables.WindowsApplied, out string? applied) && applied == RunVariables.Set;

    private static bool RawImageWritten(SequenceState state) =>
        state.Variables.TryGetValue(RunVariables.RawImageWritten, out string? written) && written == RunVariables.Set;

    private async Task PreflightAsync(RunSession session, LocalDisk? confirmedDisk, MachineVariables machine, CancellationToken cancellationToken)
    {
        AgentRun run = session.Run;

        // The server checked the sequence when it was saved; this agent checks it again against what it can run.
        if (SequenceValidator.Validate(run.Sequence) is [var problem, ..])
        {
            throw new DeploymentStepException($"{run.SequenceName} cannot run: {problem.Message} Correct the sequence and assign it again.");
        }

        // Every step on every branch: which branches the run takes, it finds out as it goes, and nothing may be erased for
        // a run that could not finish on one of them.
        IReadOnlyList<SequenceStep> steps = SequenceTree.Nodes(run.Sequence);
        List<AgentRunImage> images = [];

        foreach (ApplyImageStep step in steps.OfType<ApplyImageStep>())
        {
            images.Add(run.Images.FirstOrDefault(image => image.ImageId == step.ImageId)
                ?? throw new DeploymentStepException($"The server sent no image for step {step.Name}. Assign the sequence again."));
        }

        List<AgentRunImage> rawImages = [.. steps.OfType<WriteRawImageStep>().Select(step => WriteRawImageStepRunner.ImageOf(run, step))];

        foreach (AgentRunImage rawImage in rawImages.DistinctBy(image => image.Sha256))
        {
            if (SecureBootGate.Refusal(rawImage, run.AllowSecureBootMismatch, session.SecureBootEnabled, session.TrustedUefiCas) is { } refusal)
            {
                throw new DeploymentStepException(refusal);
            }

            if (SecureBootGate.Warning(rawImage, run.AllowSecureBootMismatch, session.SecureBootEnabled, session.TrustedUefiCas) is { } warning)
            {
                log.Warning(warning);
            }

            if (SecureBootGate.Unknown(rawImage, session.SecureBootEnabled, session.TrustedUefiCas) is { } unknown)
            {
                log.Warning(unknown);
            }
        }

        if (rawImages.Count > 0)
        {
            // A seed step that runs whatever happens has to have every value it uses, which is known now. One with
            // conditions, inside a group, IF or repeat, or that lets the run go on when it fails, is left to its step.
            foreach (WriteCloudInitSeedStep seed in run.Sequence.Steps.OfType<WriteCloudInitSeedStep>()
                .Where(seed => seed.Conditions.Count == 0 && seed.When is null && !seed.ContinueOnError))
            {
                WriteCloudInitSeedStepRunner.Render(seed, run.ComputerName, machine);
            }
        }

        if (steps.OfType<InjectDriversStep>().Any() && !File.Exists(InjectDriversStepRunner.DismIn(systemDirectory)))
        {
            throw new DeploymentStepException(InjectDriversStepRunner.NoDismMessage);
        }

        if (steps.OfType<RunScriptStep>().Any(step => step is { Interpreter: ScriptInterpreter.PowerShell, Phase: SequencePhase.WindowsPE })
            && !File.Exists(RunScriptStepRunner.PowerShellIn(systemDirectory)))
        {
            throw new DeploymentStepException(RunScriptStepRunner.NoPowerShellMessage);
        }

        if (steps.Any(step => step.ErasesDisk))
        {
            IReadOnlyList<LocalDisk> disks = await partitioner.ListDisksAsync(cancellationToken).ConfigureAwait(false);
            LocalDisk disk = SelectDisk(run.DiskNumber, confirmedDisk, disks);

            if (steps.Any(step => step.IsContainer))
            {
                CheckTreeSize(run, disk, rawImages.Count > 0);
            }
            else if (rawImages.Count == 0)
            {
                CheckSize(run, disk, images, steps.OfType<PartitionStep>().FirstOrDefault());
            }
            else
            {
                CheckRawSize(run, disk, rawImages[0], steps.Any(step => step is WriteCloudInitSeedStep));
            }

            if (rawImages.Count > 0)
            {
                using IRawDisk raw = rawDisks.Open(disk);

                if (raw.SectorSize != GptLayout.SectorSize)
                {
                    throw new DeploymentStepException($"Disk {disk.Number} {WriteRawImageStepRunner.FourKilobyteSectorsMessage}");
                }
            }

            session.Disk = disk;
        }

        if (images.Count > 0)
        {
            applier.Prepare();
        }

        foreach (AgentRunImage image in images.Concat(rawImages).DistinctBy(image => image.Sha256))
        {
            long? length = await ServerCallRules.CallAsync(
                call => server.HeadRunFileAsync(session.MachineId, session.Tokens.Token, run.Id, image.Sha256, call),
                "the image check",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);

            if (length is null)
            {
                log.Warning($"The server did not say how large {image.Name} is. The download checks it instead.");
            }
            else if (length != image.SizeBytes)
            {
                throw new DeploymentStepException(
                    $"The server's file for {image.Name} holds {length} bytes instead of {image.SizeBytes}. Upload the image again.");
            }
        }

        log.Information(session.Disk is { } chosen ? $"Running {run.SequenceName} on {chosen.Describe()}." : $"Running {run.SequenceName}.");
    }

    // The size rule: the partitions, each image's download and installed files, each package with room to unpack it,
    // and some to spare.
    private static void CheckSize(AgentRun run, LocalDisk disk, IReadOnlyList<AgentRunImage> images, PartitionStep? partition)
    {
        int system = partition?.SystemPartitionMegabytes ?? DiskpartScript.SystemPartitionMegabytes;
        int recovery = partition?.RecoveryPartitionMegabytes ?? DiskpartScript.RecoveryPartitionMegabytes;
        long partitions = (system + DiskpartScript.ReservedPartitionMegabytes + recovery) * Megabyte;
        long downloads = images.Sum(image => image.SizeBytes);
        long installed = images.Sum(image => image.InstalledBytes);
        long packages = 2 * run.Packages.Sum(package => package.SizeBytes);
        long required = partitions + downloads + installed + packages + SpareBytes;

        if (disk.SizeBytes >= required)
        {
            return;
        }

        List<string> parts = [$"{ByteSize.Format(partitions)} for the boot and recovery partitions"];

        if (images.Count > 0)
        {
            parts.Add($"{ByteSize.Format(downloads)} for the image downloads");
            parts.Add($"{ByteSize.Format(installed)} for the installed files");
        }

        if (packages > 0)
        {
            parts.Add($"{ByteSize.Format(packages)} for the packages and their contents");
        }

        throw new DeploymentStepException(
            $"Disk {disk.Number} holds {ByteSize.Format(disk.SizeBytes)}, but {run.SequenceName} needs {ByteSize.Format(required)}: " +
            $"{string.Join(", ", parts)} and {ByteSize.Format(SpareBytes)} to spare. Run it on a larger disk.");
    }

    // A tree needs what the path through it that needs the most does, as SequenceSizes works it out: an IF takes the
    // branch that needs more. The rules for each step are those of a list: an image's download and installed files, a
    // package with room to unpack it, some to spare, and a raw disk image's disk with nothing to spare, since it is
    // written as it downloads. A package the server sent for no step of the tree counts on every path, as in a list.
    private static void CheckTreeSize(AgentRun run, LocalDisk disk, bool raw)
    {
        HashSet<Guid> nodes = [.. SequenceTree.Nodes(run.Sequence).Select(node => node.Id)];

        long Packages(Func<AgentRunPackage, bool> which) => raw ? 0 : 2 * run.Packages.Where(which).Sum(package => package.SizeBytes);

        long FileBytes(SequenceStep step) => step switch
        {
            ApplyImageStep apply when !raw => run.Images.FirstOrDefault(image => image.ImageId == apply.ImageId) is { } image
                ? image.SizeBytes + image.InstalledBytes
                : 0,
            WriteRawImageStep write => WriteRawImageStepRunner.ImageOf(run, write).InstalledBytes,
            _ => Packages(package => package.StepId == step.Id),
        };

        long required = SequenceSizes.RequiredBytes(run.Sequence, FileBytes) + Packages(package => !nodes.Contains(package.StepId)) + (raw ? 0 : SpareBytes);

        if (disk.SizeBytes >= required)
        {
            return;
        }

        throw new DeploymentStepException(
            $"Disk {disk.Number} holds {ByteSize.Format(disk.SizeBytes)}, but {run.SequenceName} needs {ByteSize.Format(required)} on the " +
            $"path through it that needs the most{(raw ? "" : $", {ByteSize.Format(SpareBytes)} to spare included")}. Run it on a larger disk.");
    }

    // Where a run goes on after a restart: at the step its state names, by number in a list and by name in a tree.
    private static string WhereItGoesOn(SequenceState state, int count)
    {
        if (state.Format < SequenceState.TreeFormat)
        {
            return $"at step {Math.Min(state.NextIndex + 1, count)} of {count}";
        }

        return state.Cursor is { } cursor && SequenceTree.Index(state.Definition).TryGetValue(cursor.NodeId, out NodePosition? position)
            ? cursor.Leaving ? $"after the steps of {position.Step.Name}" : $"at step {position.Step.Name}"
            : "at its end";
    }

    // A raw disk image is written as it downloads, so the disk needs room for the disk it holds and the seed, and no more.
    private static void CheckRawSize(AgentRun run, LocalDisk disk, AgentRunImage image, bool seed)
    {
        long required = image.InstalledBytes + (seed ? CloudInitSeed.DiskBytes : 0);

        if (disk.SizeBytes >= required)
        {
            return;
        }

        throw new DeploymentStepException(
            $"Disk {disk.Number} holds {ByteSize.Format(disk.SizeBytes)}, but {run.SequenceName} needs {ByteSize.Format(required)}: " +
            $"{ByteSize.Format(image.InstalledBytes)} for the disk image" +
            (seed ? $" and {ByteSize.Format(CloudInitSeed.DiskBytes)} for the cloud-init seed" : "") +
            ". Run it on a larger disk.");
    }

    private static LocalDisk SelectDisk(int? diskNumber, LocalDisk? confirmedDisk, IReadOnlyList<LocalDisk> disks)
    {
        if (diskNumber is { } number)
        {
            if (confirmedDisk is null || confirmedDisk.Number != number)
            {
                throw new DeploymentStepException(
                    $"Disk {number} was chosen before the agent started again, and disk numbers can change when a machine restarts, " +
                    "so nothing was erased. Choose the sequence and the disk again at this machine.");
            }

            LocalDisk disk = disks.FirstOrDefault(candidate => candidate.Number == number)
                ?? throw new DeploymentStepException(
                    $"Disk {number}, chosen at this machine, is not a disk DDT can install on now. Restart the machine from the network and choose again.");

            if (!disk.IsSameDiskAs(confirmedDisk))
            {
                throw new DeploymentStepException(
                    $"Disk {number} is no longer the disk chosen at this machine ({confirmedDisk.DisplayModel}, " +
                    $"{ByteSize.Format(confirmedDisk.SizeBytes)}), so nothing was erased. Choose the sequence and the disk again at this machine.");
            }

            return disk;
        }

        return disks.Count switch
        {
            0 => throw new DeploymentStepException(NoDiskMessage),
            1 => disks[0],
            _ => throw new DeploymentStepException(SeveralDisksMessage),
        };
    }

    // Windows PE lettered the run's Windows volume as it chose, and the other partitions not at all. Before Partition
    // finished, the state has no partition ids, and the engine fails the interrupted Partition. The installed Windows
    // runs from the run's Windows volume, and its steps need no other.
    private async Task ResumeAsync(RunSession session, LocalRun resumed, FileRunStateStore store, CancellationToken cancellationToken)
    {
        if (_phase == SequencePhase.Windows)
        {
            session.RunningWindows = resumed.WindowsRoot;
        }
        else if (RunVariables.DiskIds(resumed.State.Variables) is { } ids)
        {
            session.Volumes = await partitioner.FindAsync(ids, resumed.WindowsRoot, cancellationToken).ConfigureAwait(false);
        }

        await store.AttachAsync(resumed.Files, CancellationToken.None).ConfigureAwait(false);
    }

    private AgentStepRunner Steps(RunSession session, FileRunStateStore store, RunHeartbeat heartbeat)
    {
        RunDownloads downloads = new(server, session, log, timeProvider, heartbeatInterval);

        return new AgentStepRunner(
            new PartitionStepRunner(partitioner, session, store, log, dryRun),
            new ApplyImageStepRunner(applier, downloads, session, log),
            new InjectDriversStepRunner(tools, downloads, session, log),
            new WriteUnattendStepRunner(server, session, heartbeat.ReportNowAsync, log, timeProvider),
            new JoinDomainStepRunner(joiner, server, session, heartbeat.ReportNowAsync, log, timeProvider),
            new RunScriptStepRunner(tools, downloads, session, log, workDirectory),
            new WriteRawImageStepRunner(partitioner, rawDisks, downloads, session, log),
            new WriteCloudInitSeedStepRunner(rawDisks, session, log, timeProvider),
            new StepAccounts(
                server,
                session,
                heartbeat.ReportNowAsync,
                accountTools ?? (dryRun ? AccountTools.DryRun(log) : AccountTools.Native(log)),
                log,
                timeProvider),
            heartbeat.TokenRejected,
            log,
            timeProvider,
            new PauseStepRunner(heartbeat, status?.Console, log, timeProvider));
    }

    // What Windows needs to go on with the run, if anything, then, as in Microsoft's sequence after applying, the applied
    // image's bcdboot and its recovery environment, and last Windows Boot Manager first in the firmware's boot order:
    // until then a restart starts the machine from the network, not into a Windows that is not ready. The hand-over comes
    // first because bcdboot may put its entry first itself, which a failed hand-over could not undo.
    private async Task MakeBootableAsync(TargetVolumes volumes, Func<Task>? handOverRun, CancellationToken cancellationToken)
    {
        if (handOverRun is not null)
        {
            await handOverRun().ConfigureAwait(false);
        }

        await bcdWriter.WriteAsync(volumes, cancellationToken).ConfigureAwait(false);
        _windowsFirst = true;
        await bcdWriter.PutWindowsFirstAsync(volumes, cancellationToken).ConfigureAwait(false);
        restartMarker.Set(RestartInto.Windows);
    }

    // A raw disk image starts from its fallback file, which a boot entry named after the image puts first, the last change
    // to the machine as for Windows. An image without an EFI system partition gets none.
    private async Task PutRawImageFirstAsync(RunSession session, SequenceState state, CancellationToken cancellationToken)
    {
        if (RunVariables.RawSystemPartitionOf(state.Variables) is not { } esp)
        {
            log.Warning(
                "The disk image has no EFI system partition, so the machine gets no boot entry for it and starts it only if its " +
                "firmware tries the disk. Set its boot order to start the disk first.");

            return;
        }

        // In a tree, the image of the branch the run took: the one whose step is done.
        IReadOnlyList<SequenceStep> nodes = SequenceTree.Nodes(state.Definition);
        HashSet<Guid> done = [.. state.Steps.Where(step => step.State == StepState.Done).Select(step => step.StepId)];
        string? name = nodes.OfType<WriteRawImageStep>()
            .OrderByDescending(step => done.Contains(step.Id))
            .Select(step => session.Run.Images.FirstOrDefault(image => image.ImageId == step.ImageId)?.Name)
            .FirstOrDefault(found => !string.IsNullOrWhiteSpace(found));
        string description = name is null ? "Linux" : name.Length > MaxBootEntryName ? name[..MaxBootEntryName] : name;

        _windowsFirst = true;
        await bcdWriter.PutFirstAsync(
            esp,
            FirmwareBootEntry.FallbackLoaderPath,
            description,
            RunVariables.ErasedSystemPartitionIdsOf(state.Variables),
            cancellationToken).ConfigureAwait(false);
        restartMarker.Set(RestartInto.Windows);
    }

    // The order matters: the log goes while the machine may still send it, and the Done report is the last call the
    // server gets. The restart follows whatever the Done report answers: the run is over either way. In Windows the run
    // ends here without a restart: the agent first removes itself with the rest of the run's directory, then restarts.
    // There the Done report waits on the disk with the token until the server has it, even when it cannot be reached
    // for a while.
    private async Task<RunResult> FinishAsync(
        RunSession session,
        FileRunStateStore store,
        RunHeartbeat heartbeat,
        SequenceState state,
        CancellationToken cancellationToken)
    {
        bool inWindows = _phase == SequencePhase.Windows;
        log.Information(inWindows ? "The run is done. Sending the last log lines." : "The run is done. Sending the last log lines, then restarting.");
        status?.RunFinished();

        AgentRunReport done = heartbeat.Snapshot(DeploymentState.Done);
        await EndRunAsync(store.Files, done).ConfigureAwait(false);

        if (!inWindows && session.RunDirectory is { } directory)
        {
            Leftovers.Delete(directory, log);
        }

        try
        {
            // Refused before the Done report was sent: the run was stopped meanwhile, so the machine must not start
            // into a Windows with the answer file.
            if (!await FlushAllAsync(heartbeat, cancellationToken).ConfigureAwait(false))
            {
                await UndoAsync(session, state).ConfigureAwait(false);
                log.Warning("The server no longer accepts this machine's token, so the run was stopped before it ended. Registering again.");

                return new RunResult(RunOutcome.TokenRejected);
            }

            await ServerCallRules.CallAsync(
                call => heartbeat.ReportAsync(done, call),
                "the end of the run",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (AgentTokenRejectedException)
        {
            // The server stores Done before it answers, so a lost answer makes the retry look like this.
            log.Warning("The server no longer accepts this machine's token. It most likely recorded the run as done already.");
        }
        catch (DeploymentStepException exception) when (inWindows && exception.InnerException is { } cause && !ServerCallRules.IsRefusal(cause))
        {
            log.Warning($"The server could not be told that the run is done ({LogText.OneLine(exception)}). It is told once it can be reached.");

            return new RunResult(RunOutcome.Finished, done);
        }
        catch (Exception exception)
        {
            log.Warning($"The server could not be told that the run is done ({LogText.OneLine(exception)}). " +
                (inWindows ? "The agent removes itself anyway." : "The machine restarts anyway."));
        }

        Reported(store.Files);

        if (inWindows)
        {
            return new RunResult(RunOutcome.Finished);
        }

        return await RebootAsync(
            RestartInto.Windows,
            RestartReason.RunDone,
            RunOutcome.Finished,
            "Restart it by hand; the run is done.",
            cancellationToken).ConfigureAwait(false);
    }

    // The run's state and token are on the disk, and the rest of the run follows the next start of Windows PE, or of the
    // installed Windows. The registration after the restart decides whether the run goes on, but after the hand-over
    // the installed Windows would go on with it and use its answer file, so a refused token keeps it from starting.
    private async Task<RunResult> RestartAsync(RunHeartbeat heartbeat, RestartInto into, CancellationToken cancellationToken)
    {
        bool handingOver = _phase == SequencePhase.WindowsPE && into == RestartInto.Windows;
        heartbeat.Activity = RunActivity.Restarting;
        log.Information(_phase == SequencePhase.Windows
            ? "Windows restarts, and the run goes on after the restart."
            : handingOver
                ? "The machine restarts into the installed Windows, where the agent goes on with the run."
                : "The machine restarts into Windows PE, and the run goes on after the restart.");

        try
        {
            await ServerCallRules.CallAsync(
                call => heartbeat.ReportAsync(heartbeat.Snapshot(DeploymentState.Running), call),
                "the restart",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (AgentTokenRejectedException) when (handingOver)
        {
            return await TokenRejectedAsync(heartbeat).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            log.Warning($"The server could not be told of the restart ({LogText.OneLine(exception)}). The machine restarts anyway.");
        }

        if (!await FlushAllAsync(heartbeat, cancellationToken).ConfigureAwait(false) && handingOver)
        {
            return await TokenRejectedAsync(heartbeat).ConfigureAwait(false);
        }

        return await RebootAsync(
            into,
            handingOver ? RestartReason.HandOver : RestartReason.StepAsked,
            RunOutcome.Restarting,
            "Restart it by hand; the run goes on after the restart.",
            cancellationToken).ConfigureAwait(false);
    }

    // The run's state and answer file stay: the registration with the run token decides whether it goes on. Until then
    // the machine starts from the network.
    private async Task<RunResult> TokenRejectedAsync(RunHeartbeat heartbeat)
    {
        await RestoreBootOrderAsync().ConfigureAwait(false);
        log.Warning("The server no longer accepts this machine's token during the run. Registering again.");

        return new RunResult(RunOutcome.TokenRejected, heartbeat.Snapshot(DeploymentState.Failed, LostContactMessage));
    }

    private async Task<RunResult> RebootAsync(
        RestartInto into,
        RestartReason reason,
        RunOutcome outcome,
        string byHand,
        CancellationToken cancellationToken)
    {
        status?.Restarting(reason, into);

        try
        {
            await rebooter.RebootAsync(into, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (Exception exception)
        {
            string message = $"The machine could not restart itself ({LogText.OneLine(exception)}). {byHand}";
            log.Error(message);
            status?.RestartFailed(message);
        }

        return new RunResult(outcome);
    }

    // The run is over: nothing of it may start Windows or resume it.
    private async Task<RunResult> FailAsync(
        RunSession session,
        FileRunStateStore store,
        RunHeartbeat heartbeat,
        SequenceState state,
        string error,
        CancellationToken cancellationToken)
    {
        log.Error($"The run failed: {error}");
        status?.RunFailed(error);
        await UndoAsync(session, state).ConfigureAwait(false);
        AgentRunReport report = heartbeat.Snapshot(DeploymentState.Failed, error);
        await EndRunAsync(store.Files, report).ConfigureAwait(false);
        await FlushAllAsync(heartbeat, cancellationToken).ConfigureAwait(false);

        return await ReportFailedAsync(heartbeat, store.Files, report, cancellationToken).ConfigureAwait(false);
    }

    // A failure the server was not told about is handed back, for the loop to report once it can.
    private async Task<RunResult> ReportFailedAsync(RunHeartbeat heartbeat, RunFiles? files, AgentRunReport report, CancellationToken cancellationToken)
    {
        try
        {
            await ServerCallRules.CallAsync(
                call => heartbeat.ReportAsync(report, call),
                "the failure report",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);

            Reported(files);

            return new RunResult(RunOutcome.Failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunResult(RunOutcome.Stopped);
        }
        catch (AgentTokenRejectedException)
        {
            log.Warning("The server no longer accepts this machine's token, so the failure could not be reported. Registering again.");

            return new RunResult(RunOutcome.TokenRejected, report);
        }
        catch (Exception exception)
        {
            log.Warning($"The failure could not be reported ({LogText.OneLine(exception)}).");

            return new RunResult(RunOutcome.Failed, report);
        }
    }

    // The run is over, and report is what the server is to learn of it. In Windows PE the run's files go at once, the
    // token first. In the installed Windows the token stays with the report until the server has it, as nothing else
    // could tell the server after a stop, or once it can be reached again: the next start sends it. Should the report
    // not reach the disk, the files go all the same, so nothing goes on with a run that is over.
    private async Task EndRunAsync(RunFiles? files, AgentRunReport report)
    {
        if (files is null)
        {
            return;
        }

        if (_phase == SequencePhase.Windows)
        {
            try
            {
                await files.SaveFinalReportAsync(report, CancellationToken.None).ConfigureAwait(false);

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                log.Warning($"The run's last report could not be kept on the disk ({exception.Message}), so it is lost should the agent stop before the server has it.");
            }
        }

        files.Discard();
    }

    // The server has the run's last report, or will never take it.
    private void Reported(RunFiles? files)
    {
        if (_phase == SequencePhase.Windows)
        {
            files?.Discard();
        }
    }

    // The answer file holds passwords, and a machine whose run did not finish keeps its disk until it runs again: it
    // must start from the network, not into a Windows without its answer file. The boot order goes back even after a
    // stop. A restart back into Windows PE that the run asked for is not due either.
    private async Task UndoAsync(RunSession session, SequenceState state)
    {
        if (session.Volumes is { } volumes)
        {
            LocalRun.DeleteAnswerFile(state, volumes.Windows, log);
        }

        if (_phase == SequencePhase.WindowsPE)
        {
            restartMarker.Clear();
        }

        await RestoreBootOrderAsync().ConfigureAwait(false);
    }

    // With the boot order back, a restart would start the network, not the installed Windows, so no restart into it is
    // due any more.
    private async Task RestoreBootOrderAsync()
    {
        if (_windowsFirst)
        {
            _windowsFirst = false;
            restartMarker.Clear();
            await bcdWriter.RestoreBootOrderAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    // Until the queue is empty, but never for long: the lines are worth less than the restart that follows. False
    // when the server refused the token.
    private async Task<bool> FlushAllAsync(RunHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        int failures = 0;

        for (int attempt = 0; attempt < MaxFinalFlushes && log.QueuedLines > 0; attempt++)
        {
            try
            {
                await heartbeat.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (AgentTokenRejectedException)
            {
                return false;
            }
            catch (Exception exception) when (ServerCallRules.IsRefusal(exception))
            {
                return true;
            }
            catch (Exception exception) when (ServerCallRules.IsTransient(exception, cancellationToken))
            {
                if (++failures > MaxFinalFlushFailures)
                {
                    return true;
                }

                await Task.Delay(AgentLimits.RetryDelay(failures), timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return true;
            }
        }

        return true;
    }
}

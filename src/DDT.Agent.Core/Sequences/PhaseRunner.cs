// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Runs the steps of the current phase with the engine while the heartbeat beats. It ends the phase as the engine's
// outcome says: restarting, handing over to the installed system, finished or failed.
internal sealed class PhaseRunner(
    Func<SequenceRun, IStepRunner> steps,
    RunInputsWait inputs,
    IHandOver handOver,
    RunBootOrder bootOrder,
    RunEnding ending,
    AgentLog log)
{
    // How often the boot environment may start instead of the installed system and hand the run over again.
    private const int MaxWindowsPEReturns = 3;

    public async Task<RunResult> RunAsync(SequenceRun run, CancellationToken cancellationToken)
    {
        RunHeartbeat heartbeat = run.Heartbeat;
        SequenceEngine engine = new(steps(run), run.Store, heartbeat);
        SequenceOutcome outcome = SequenceOutcome.Failed;
        string? error = null;

        // The engine saved the step that asked for a restart as done. Once that's on disk, only the restart itself
        // keeps the next steps from running without it.
        bool restartDue = false;

        using CancellationTokenSource stepping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        heartbeat.Activity = run.WaitsForInputs ? RunActivity.WaitingForInput : RunActivity.Step;
        heartbeat.Start(stepping, cancellationToken);

        try
        {
            await TakeValuesAsync(run, stepping.Token).ConfigureAwait(false);

            SequenceRunResult result = !SequenceKinds.IsBoot(run.State.Phase) && !run.InInstalledSystem
                ? await HandOverAgainAsync(run).ConfigureAwait(false)
                : await engine.RunAsync(run.State, run.Machine, stepping.Token).ConfigureAwait(false);
            run.State = result.State;
            outcome = result.Outcome;
            error = result.Error;
            restartDue = outcome == SequenceOutcome.RebootRequired;

            if (restartDue)
            {
                run.RecordRestart();
            }

            await FinishPhaseAsync(run, outcome, stepping.Token).ConfigureAwait(false);

            // A beat still on its way can be refused, for example because an operator stopped the run. That still ends
            // the run before the last reports.
            await heartbeat.StopAsync().ConfigureAwait(false);
            stepping.Token.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            await heartbeat.StopAsync().ConfigureAwait(false);
            outcome = SequenceOutcome.Failed;
            error = LogText.OneLine(exception);
            run.State = run.Store.State ?? run.State;
        }

        return await ResolveOutcomeAsync(run, outcome, error, restartDue, cancellationToken).ConfigureAwait(false);
    }

    // Conditions and scripts read the values the run starts with. They arrive once its inputs are answered. For a run
    // this agent got before it started, they come with the answer to the report that started it. A fresh run's seeds
    // are checked against them.
    private async Task TakeValuesAsync(SequenceRun run, CancellationToken cancellationToken)
    {
        if (run.WaitsForInputs)
        {
            run.Machine = run.Machine with { Variables = await inputs.WaitAsync(run.Session, run.Heartbeat, cancellationToken).ConfigureAwait(false) };
            run.Heartbeat.Activity = RunActivity.Step;
        }
        else if (run.Machine.Variables is null && run.Heartbeat.Values is { } started)
        {
            run.Machine = run.Machine with { Variables = started };
        }

        if (run.Resumed is null)
        {
            RunPreflight.CheckSeeds(run.Session.Run, run.Machine);
        }
    }

    // The hand-over was interrupted, or the firmware started the network first. Doing it again can't harm what the
    // first attempt did.
    private async Task<SequenceRunResult> HandOverAgainAsync(SequenceRun run)
    {
        SequenceState state = run.State;
        int returns = int.TryParse(state.Variables.GetValueOrDefault(RunVariables.WindowsPEReturns), NumberStyles.None, CultureInfo.InvariantCulture, out int earlier)
            ? earlier + 1
            : 1;

        if (returns > MaxWindowsPEReturns)
        {
            return new SequenceRunResult(SequenceOutcome.Failed, state, EnvironmentTexts.InstalledSystemDidNotStart(run.Session.Environment));
        }

        log.Warning(EnvironmentTexts.HandingOverAgain(run.Session.Environment, returns, MaxWindowsPEReturns));

        Dictionary<string, string> variables = new(state.Variables, StringComparer.Ordinal)
        {
            [RunVariables.WindowsPEReturns] = returns.ToString(CultureInfo.InvariantCulture),
        };
        state = state with { Phase = run.Phase, Variables = variables };
        await run.Store.SaveAsync(state, CancellationToken.None).ConfigureAwait(false);

        return new SequenceRunResult(SequenceOutcome.PhaseChangeRequired, state, null);
    }

    // What the outcome needs before the phase ends: the boot order, the hand-over, or the run token for the restart.
    private async Task FinishPhaseAsync(SequenceRun run, SequenceOutcome outcome, CancellationToken cancellationToken)
    {
        switch (outcome)
        {
            case SequenceOutcome.Completed when !run.InInstalledSystem && IsSet(run.State, RunVariables.WindowsApplied):
                run.Heartbeat.Activity = RunActivity.Finishing;
                await bootOrder.MakeBootableAsync(run, run.Session.RequireVolumes(), null, cancellationToken).ConfigureAwait(false);
                break;
            case SequenceOutcome.Completed when !run.InInstalledSystem && IsSet(run.State, RunVariables.RawImageWritten):
                run.Heartbeat.Activity = RunActivity.Finishing;
                await bootOrder.PutRawImageFirstAsync(run, cancellationToken).ConfigureAwait(false);
                break;
            case SequenceOutcome.Completed:
                run.Heartbeat.Activity = RunActivity.Finishing;
                break;
            case SequenceOutcome.PhaseChangeRequired when run.InInstalledSystem:
                throw new DeploymentStepException(EnvironmentTexts.BootStepAfterInstalled(run.Session.Environment));
            case SequenceOutcome.PhaseChangeRequired:
                run.Heartbeat.Activity = RunActivity.HandingOver;
                await handOver.HandOverAsync(run, bootOrder, cancellationToken).ConfigureAwait(false);
                break;
            case SequenceOutcome.RebootRequired:
                run.Heartbeat.Activity = RunActivity.Restarting;
                await run.Store.SaveTokenAsync(CancellationToken.None).ConfigureAwait(false);
                break;
        }
    }

    private async Task<RunResult> ResolveOutcomeAsync(
        SequenceRun run,
        SequenceOutcome outcome,
        string? error,
        bool restartDue,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            await bootOrder.RestoreAsync(run).ConfigureAwait(false);
            log.Warning(EnvironmentTexts.Stopped(run.Session.Environment));

            return new RunResult(RunOutcome.Stopped);
        }

        Exception? failure = run.Heartbeat.Failure;

        if (failure is AgentTokenRejectedException && restartDue)
        {
            log.Warning("The server no longer accepts this machine's token. The machine restarts as the run asked, and the run goes on after the restart if the server still runs it.");

            return await ending.RebootAsync(
                run.SamePhase,
                RestartReason.StepAsked,
                RunOutcome.Restarting,
                "Restart it by hand; the run goes on after the restart.",
                cancellationToken).ConfigureAwait(false);
        }

        if (failure is AgentTokenRejectedException)
        {
            return await ending.TokenRejectedAsync(run).ConfigureAwait(false);
        }

        if (failure is { } refusal)
        {
            return await FailAsync(run, LogText.OneLine(refusal), cancellationToken).ConfigureAwait(false);
        }

        return outcome switch
        {
            SequenceOutcome.Completed => await ending.FinishAsync(run, cancellationToken).ConfigureAwait(false),
            SequenceOutcome.RebootRequired => await ending.RestartAsync(run, run.SamePhase, cancellationToken).ConfigureAwait(false),
            SequenceOutcome.PhaseChangeRequired => await ending.RestartAsync(run, RestartInto.InstalledSystem, cancellationToken).ConfigureAwait(false),
            SequenceOutcome.Stopped => new RunResult(RunOutcome.Stopped),
            _ => await FailAsync(run, error ?? "The run failed.", cancellationToken).ConfigureAwait(false),
        };
    }

    // The run is over, also where a hand-over staged it for the installed system. A stopped run and one whose token
    // was refused may still go on, so theirs stays.
    private Task<RunResult> FailAsync(SequenceRun run, string error, CancellationToken cancellationToken)
    {
        handOver.Withdraw(run);

        return ending.FailAsync(run, error, cancellationToken);
    }

    private static bool IsSet(SequenceState state, string variable) =>
        state.Variables.TryGetValue(variable, out string? value) && value == RunVariables.Set;
}

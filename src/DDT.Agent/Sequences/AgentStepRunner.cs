// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Runs each step with the IStepKindRunner for its kind, inside the step's accounts, with the step's id on its log lines.
// A step that throws fails with the message; a stop and a 401 go on to the engine, a 401 through tokenRejected first.
public sealed class AgentStepRunner(
    IReadOnlyList<IStepKindRunner> runners,
    StepAccounts accounts,
    Action<AgentTokenRejectedException> tokenRejected,
    AgentLog log,
    TimeProvider timeProvider) : IStepRunner
{
    public const string JoinDomainInWindowsPE =
        "Joining the domain runs in Windows, after the hand-over, but this agent was asked to run it in Windows PE.";

    public const string UnknownKind = "This agent cannot run this kind of step.";

    public async Task<StepResult> RunAsync(SequenceStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        log.StepId = step.Id;

        try
        {
            log.Information($"Step {step.Name} begins.");
            long started = timeProvider.GetTimestamp();
            StepResult result;

            try
            {
                result = await accounts.RunAsync(step, context, account => RunKindAsync(step, context, account, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                log.Warning($"Step {step.Name} was stopped.");

                throw;
            }
            catch (AgentTokenRejectedException exception)
            {
                log.Warning($"Step {step.Name} was stopped: the server no longer accepts this machine's token.");
                tokenRejected(exception);

                throw;
            }
            catch (Exception exception)
            {
                result = StepResult.Failed(LogText.OneLine(exception));
            }

            LogResult(step, result, LogText.Duration(timeProvider.GetElapsedTime(started)));

            return result;
        }
        finally
        {
            log.StepId = null;
        }
    }

    // A Restart step needs no runner, and without one for its kind a step fails.
    private async Task<StepResult> RunKindAsync(SequenceStep step, StepContext context, IAccountSession? account, CancellationToken cancellationToken)
    {
        if (step is RebootStep)
        {
            return StepResult.RebootRequired();
        }

        if (step is JoinDomainStep && context.Phase == SequencePhase.WindowsPE)
        {
            return StepResult.Failed(JoinDomainInWindowsPE);
        }

        return runners.FirstOrDefault(runner => runner.Runs(step)) is { } kind
            ? await kind.RunAsync(step, context, account, cancellationToken).ConfigureAwait(false)
            : StepResult.Failed(UnknownKind);
    }

    private void LogResult(SequenceStep step, StepResult result, string duration)
    {
        switch (result.Outcome)
        {
            case StepOutcome.Failed:
                log.Error($"Step {step.Name} failed after {duration}: {result.Error}");
                break;
            case StepOutcome.RebootRequired:
                log.Information($"Step {step.Name} finished after {duration}. The machine restarts before the next step.");
                break;
            default:
                log.Information($"Step {step.Name} finished after {duration}.");
                break;
        }
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Runs each step with the runner for its kind, inside the accounts it uses, and names the step in every log line
// meanwhile. A step that throws fails with the exception's message; after a stop the exception goes on, which the engine
// takes as the stop. A 401 goes to tokenRejected, which stops the run as a refused beat does, and then on to the engine
// too.
public sealed class AgentStepRunner(
    PartitionStepRunner partition,
    ApplyImageStepRunner applyImage,
    InjectDriversStepRunner injectDrivers,
    WriteUnattendStepRunner writeUnattend,
    JoinDomainStepRunner joinDomain,
    RunScriptStepRunner runScript,
    WriteRawImageStepRunner writeRawImage,
    WriteCloudInitSeedStepRunner writeCloudInitSeed,
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
                result = await accounts.RunAsync(step, context, async account => step switch
                {
                    PartitionStep partitionStep => await partition.RunAsync(partitionStep, context, cancellationToken).ConfigureAwait(false),
                    ApplyImageStep applyImageStep => await applyImage.RunAsync(applyImageStep, context, cancellationToken).ConfigureAwait(false),
                    InjectDriversStep injectDriversStep => await injectDrivers.RunAsync(injectDriversStep, context, cancellationToken).ConfigureAwait(false),
                    WriteUnattendStep writeUnattendStep => await writeUnattend.RunAsync(writeUnattendStep, context, cancellationToken).ConfigureAwait(false),
                    RunScriptStep runScriptStep => await runScript.RunAsync(runScriptStep, context, account, cancellationToken).ConfigureAwait(false),
                    WriteRawImageStep writeRawImageStep => await writeRawImage.RunAsync(writeRawImageStep, context, cancellationToken).ConfigureAwait(false),
                    WriteCloudInitSeedStep seedStep => await writeCloudInitSeed.RunAsync(seedStep, context, cancellationToken).ConfigureAwait(false),
                    RebootStep => StepResult.RebootRequired(),
                    JoinDomainStep when context.Phase == SequencePhase.WindowsPE => StepResult.Failed(JoinDomainInWindowsPE),
                    JoinDomainStep joinDomainStep => await joinDomain.RunAsync(joinDomainStep, context, cancellationToken).ConfigureAwait(false),
                    _ => StepResult.Failed(UnknownKind),
                }, cancellationToken).ConfigureAwait(false);
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

            string duration = LogText.Duration(timeProvider.GetElapsedTime(started));

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

            return result;
        }
        finally
        {
            log.StepId = null;
        }
    }
}

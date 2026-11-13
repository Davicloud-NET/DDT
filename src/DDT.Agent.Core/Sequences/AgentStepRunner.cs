// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Runs each step with the IStepKindRunner for its kind, inside the step's accounts, with the step's id on its log
// lines. A step that throws fails with the message. A stop and a 401 are passed on to the engine, and a 401 goes
// through tokenRejected first.
public sealed class AgentStepRunner(
    IReadOnlyList<IStepKindRunner> runners,
    StepAccounts accounts,
    Action<AgentTokenRejectedException> tokenRejected,
    AgentLog log,
    TimeProvider timeProvider) : IStepRunner
{
    public const string UnknownKind = "This agent cannot run this kind of step.";

    public static string JoinDomainInBoot(SequencePhase phase) =>
        $"Joining the domain runs in Windows, after the hand-over, but this agent was asked to run it in {AgentEnvironments.OfBootPhase(phase)}.";

    public const string JoinDomainInLinux = "Joining the domain runs in Windows, but this agent was asked to run it in the installed Linux.";

    public static string ApplicationsInBoot(SequencePhase phase) =>
        $"Applications install in Windows, after the hand-over, but this agent was asked to install them in {AgentEnvironments.OfBootPhase(phase)}.";

    public const string ApplicationsInLinux = "Applications install in Windows, but this agent was asked to install them in the installed Linux.";

    public static string FeaturesInBoot(SequencePhase phase) =>
        $"Roles and features are enabled in Windows, after the hand-over, but this agent was asked to enable them in {AgentEnvironments.OfBootPhase(phase)}.";

    public const string FeaturesInLinux = "Roles and features are enabled in Windows, but this agent was asked to enable them in the installed Linux.";

    public static string UpdatesInBoot(SequencePhase phase) =>
        $"Windows updates install in Windows, after the hand-over, but this agent was asked to install them in {AgentEnvironments.OfBootPhase(phase)}.";

    public const string UpdatesInLinux = "Windows updates install in Windows, but this agent was asked to install them in the installed Linux.";

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
                result = await accounts.RunAsync(step, context, resources => RunKindAsync(step, context, resources, cancellationToken), cancellationToken)
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

    // A Restart step needs no runner. Any other step without a runner for its kind fails.
    private async Task<StepResult> RunKindAsync(SequenceStep step, StepContext context, StepResources resources, CancellationToken cancellationToken)
    {
        if (step is RebootStep)
        {
            return StepResult.RebootRequired();
        }

        if (context.Phase != SequencePhase.Windows && OnlyInWindows(step, context.Phase) is { } elsewhere)
        {
            return StepResult.Failed(elsewhere);
        }

        return runners.FirstOrDefault(runner => runner.Runs(step)) is { } kind
            ? await kind.RunAsync(step, context, resources, cancellationToken).ConfigureAwait(false)
            : StepResult.Failed(UnknownKind);
    }

    // Why a step that only runs in the installed Windows cannot run in this phase, or null for any other step.
    private static string? OnlyInWindows(SequenceStep step, SequencePhase phase) => step switch
    {
        JoinDomainStep => SequenceKinds.IsBoot(phase) ? JoinDomainInBoot(phase) : JoinDomainInLinux,
        InstallApplicationsStep => SequenceKinds.IsBoot(phase) ? ApplicationsInBoot(phase) : ApplicationsInLinux,
        EnableFeaturesStep => SequenceKinds.IsBoot(phase) ? FeaturesInBoot(phase) : FeaturesInLinux,
        WindowsUpdateStep => SequenceKinds.IsBoot(phase) ? UpdatesInBoot(phase) : UpdatesInLinux,
        _ => null,
    };

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
            case StepOutcome.RestartAndContinue:
                log.Information($"Step {step.Name} restarts the machine after {duration}, and goes on after the restart.");
                break;
            default:
                log.Information($"Step {step.Name} finished after {duration}.");
                break;
        }
    }
}

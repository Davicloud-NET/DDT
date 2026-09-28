// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using DDT.Core.Templates;

namespace DDT.Agent.Sequences;

// Waits at a Pause step until someone continues at the machine or on the web, or ContinueAfterMinutes pass; the first
// wins and the others are called off. A pause found running after a restart waits again, for the same visit.
public sealed class PauseStepRunner(RunHeartbeat heartbeat, IMachineConsole? console, AgentLog log, TimeProvider timeProvider)
    : IStepKindRunner<PauseStep>
{
    public async Task<StepResult> RunAsync(PauseStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        if (!ValueTemplate.TryRender(step.Message ?? "", context.Machine.Value, out string message, out TemplateProblem? problem))
        {
            return StepResult.Failed($"The pause's message cannot be worked out: {problem?.Message().Text}");
        }

        // The engine saved this visit's Running mark before the step ran, and the web continues a visit by its pass.
        int pass = heartbeat.Position.State?.Steps.FirstOrDefault(state => state.StepId == step.Id)?.Pass ?? 0;
        TimeSpan? after = step.ContinueAfterMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null;
        heartbeat.Pause(message);

        try
        {
            // At once, so the machine's page shows the pause and its message.
            await heartbeat.ReportNowAsync(cancellationToken).ConfigureAwait(false);
            log.Information(after is { } limit
                ? $"The run pauses until someone continues it at this machine or on the web, or for {Minutes(limit)} at most."
                : "The run pauses until someone continues it at this machine or on the web.");

            using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task web = heartbeat.WaitForContinueAsync(step.Id, pass, waiting.Token);
            Task<bool> atMachine = AskAsync(step, message, waiting.Token);
            Task timer = Task.Delay(after ?? Timeout.InfiniteTimeSpan, timeProvider, waiting.Token);
            Task first = await Task.WhenAny(web, atMachine, timer).ConfigureAwait(false);

            // Nobody can answer at this machine, so the web or the time decides.
            if (first == atMachine && !cancellationToken.IsCancellationRequested && !(atMachine.IsCompletedSuccessfully && atMachine.Result))
            {
                first = await Task.WhenAny(web, timer).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await waiting.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(web, atMachine, timer).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            log.Information(first == web
                ? "Someone continued the run on the web."
                : first == atMachine ? "Someone continued the run at this machine." : $"The run goes on after {Minutes(after!.Value)}.");
            context.Progress.Report(100);

            return StepResult.Done();
        }
        finally
        {
            heartbeat.Resume();
        }
    }

    // True once someone continued at the machine; false when nobody can answer there or the question was called off. The
    // console in DDT's session asks even while none is connected, as Windows may start its session any moment.
    private async Task<bool> AskAsync(PauseStep step, string message, CancellationToken cancellationToken)
    {
        if (console is null || !(console.CanAsk || console is SessionMachineConsole))
        {
            return false;
        }

        try
        {
            while (await console.AskAsync(new PauseQuestion(step.Name, message), cancellationToken).ConfigureAwait(false) is { } answer)
            {
                if (answer.Continue)
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log.Warning($"The console at this machine cannot ask to go on ({exception.Message}). The run can be continued on the web.");
        }

        return false;
    }

    private static string Minutes(TimeSpan time) =>
        time.TotalMinutes == 1 ? "1 minute" : string.Create(CultureInfo.InvariantCulture, $"{time.TotalMinutes:0} minutes");
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;

namespace DDT.Agent.Sequences;

// A run waits at its start for the answers to its inputs, given at the machine or on the machine's page. Either way
// its values come from the server once nothing is pending. No answer is ever logged, and an Account input's goes to
// the server alone.
internal sealed class RunInputsWait(IAgentServer server, IMachineConsole? console, AgentLog log, TimeProvider timeProvider)
{
    public async Task<IReadOnlyDictionary<string, string>> WaitAsync(RunSession session, RunHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        AgentRun run = session.Run;
        Pending pending = new([.. run.PendingInputs ?? []]);

        // A report's answer brings the values when the web answered, and takes the question here away.
        Task<IReadOnlyDictionary<string, string>> fromWeb = heartbeat.WaitForValuesAsync(cancellationToken);

        log.Information($"The run waits for answers to {string.Join(", ", pending.Inputs.Select(input => input.Label))}, at this machine or on the machine's page.");

        while (!fromWeb.IsCompleted && pending.Inputs.Count > 0 && console is not null && (console.CanAsk || console is SessionMachineConsole))
        {
            if (await AskAsync(console, run, pending, fromWeb, cancellationToken).ConfigureAwait(false) is not { } answer)
            {
                break;
            }

            if (answer.Values is not { } given)
            {
                continue;
            }

            pending.Errors = InputQuestions.Check(pending.Inputs, given);
            pending.Error = null;

            if (pending.Errors.Count > 0)
            {
                log.Warning($"Answer these again: {string.Join(", ", pending.Inputs.Where(input => pending.Errors.ContainsKey(input.Name)).Select(input => input.Label))}.");

                continue;
            }

            if (await SendAsync(session, heartbeat, pending, given, cancellationToken).ConfigureAwait(false) is { } started)
            {
                return started;
            }
        }

        IReadOnlyDictionary<string, string> values = await fromWeb.ConfigureAwait(false);
        log.Information("The inputs were answered on the web, and the run starts.");

        return values;
    }

    // Null once the web answered first, or nobody can answer here after all: then the web decides.
    private static async Task<ConsoleAnswer?> AskAsync(
        IMachineConsole console,
        AgentRun run,
        Pending pending,
        Task fromWeb,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource asking = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        InputsQuestion question = new(
            run.SequenceName,
            [.. pending.Inputs.Select(input => InputQuestions.ToConsole(input, pending.Errors.GetValueOrDefault(input.Name), run.Sequence))],
            pending.Error);
        Task<ConsoleAnswer?> asked = console.AskAsync(question, asking.Token);

        if (await Task.WhenAny(asked, fromWeb).ConfigureAwait(false) == fromWeb)
        {
            await asking.CancelAsync().ConfigureAwait(false);
            await ((Task)asked).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            return null;
        }

        return await asked.ConfigureAwait(false);
    }

    // The run's values once the server took the answers; otherwise null, with what it did not take to ask again.
    private async Task<IReadOnlyDictionary<string, string>?> SendAsync(
        RunSession session,
        RunHeartbeat heartbeat,
        Pending pending,
        IReadOnlyList<ConsoleInputValue> given,
        CancellationToken cancellationToken)
    {
        try
        {
            AgentAnswersResult result = await ServerCallRules.CallAsync(
                call => server.AnswerRunInputsAsync(session.MachineId, session.Tokens.Token, session.Run.Id, new AgentInputAnswers(InputQuestions.Answers(pending.Inputs, given)), call),
                "the answers",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);

            if (result.Values is { } started)
            {
                log.Information("The server took the answers, and the run starts.");

                return started;
            }

            pending.Inputs = [.. result.InputsPending ?? []];
            pending.Errors = (result.Problems ?? []).Where(problem => problem is not null).GroupBy(problem => problem.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(problems => problems.Key, problems => problems.First().Message, StringComparer.OrdinalIgnoreCase);

            if (pending.Errors.Count > 0)
            {
                log.Warning($"The server did not take the answers to {string.Join(", ", pending.Errors.Keys)}.");
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
            pending.Error = exception.Message;
            pending.Errors = InputQuestions.FieldErrors(pending.Inputs, (exception.InnerException as AgentRequestException)?.FieldErrors) ?? new Dictionary<string, string>();
            log.Warning($"The answers were not taken: {exception.Message}");
        }

        return null;
    }

    // What is still to answer, and what was wrong with the last answers.
    private sealed class Pending(IReadOnlyList<AgentInput> inputs)
    {
        public IReadOnlyList<AgentInput> Inputs { get; set; } = inputs;

        public IReadOnlyDictionary<string, string> Errors { get; set; } = new Dictionary<string, string>();

        public string? Error { get; set; }
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Agents;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Core.Sequences;
using DDT.Core.Values;
using DDT.Server.Accounts;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Rules;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// The answers to a run's inputs and the values the run works with. An answer is checked against the input it answers, as
// the run's own copy of the sequence declares it, and kept as a RunAnswer; an Account input's answer is a RunCredential
// and never a value. The values are worked out as MachineValues does, from the run's answers, and whatever a required
// input still lacks keeps the run from starting: the run waits for an input the machine asks, and cannot start for one
// only the web asks. Nothing here saves.
public sealed class RunValues(DdtDbContext database, MachineValues machineValues, RunCredentials credentials)
{
    // The field of a validation problem about the answer to an input, such as answers.Owner, for an Account input's user
    // name and password alike. The console asks an input again by it.
    public const string AnswersField = "answers";

    // Checks answers as they come, before anything is kept: each names an input of the sequence that is asked here, once,
    // with an answer its kind takes. An answer without a value, or an Account answer without a user name and a password,
    // is none. Required inputs are the caller's to check, since the values can answer them.
    public static IReadOnlyList<AnswerProblem> Check(
        SequenceDefinition definition,
        IReadOnlyList<InputAnswer?>? answers,
        Func<InputDeclaration, bool> askedHere)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(askedHere);

        List<AnswerProblem> problems = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (InputAnswer? answer in answers ?? [])
        {
            if (answer is null || string.IsNullOrWhiteSpace(answer.Name))
            {
                problems.Add(new AnswerProblem("", AnswersField, ServerMessages.DeploymentAnswerNoInput.With()));

                continue;
            }

            string name = answer.Name.Trim();
            string field = $"{AnswersField}.{name}";

            if (Input(definition, name) is not { } input)
            {
                problems.Add(new AnswerProblem(name, field, ServerMessages.DeploymentAnswerUnknown.With("name", name)));

                continue;
            }

            field = $"{AnswersField}.{input.Name}";

            if (!seen.Add(input.Name))
            {
                problems.Add(new AnswerProblem(input.Name, field, ServerMessages.DeploymentAnswerTwice.With("label", input.Label)));
            }
            else if (!askedHere(input))
            {
                problems.Add(new AnswerProblem(
                    input.Name,
                    field,
                    ServerMessages.DeploymentAnswerAskedElsewhere.With("label", input.Label, "where", input.AskAt == InputAsk.Web ? "web" : "machine")));
            }
            else if (input.Kind == InputKind.Account)
            {
                if (IsAnswered(input, answer))
                {
                    if (AccountRules.UserNameProblem(answer.UserName) is { } userName)
                    {
                        problems.Add(new AnswerProblem(input.Name, field, userName));
                    }

                    if (AccountRules.PasswordProblem(answer.Password) is { } password)
                    {
                        problems.Add(new AnswerProblem(input.Name, field, password));
                    }
                }
            }
            else if (IsAnswered(input, answer) && ValueProblem(input, answer.Value!) is { } problem)
            {
                problems.Add(new AnswerProblem(input.Name, field, problem));
            }
        }

        return problems;
    }

    // The run's answers with these given, each input's latest answer once, in the order of the inputs. Values are kept as
    // the input spells them: a choice as declared, yes or no as true or false. Account answers are left out.
    public static IReadOnlyList<RunAnswer> Merge(
        SequenceDefinition definition,
        IReadOnlyList<RunAnswer> before,
        IReadOnlyList<InputAnswer?>? answers,
        string? answeredBy,
        bool atMachine,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(before);

        Dictionary<string, RunAnswer> byName = new(StringComparer.OrdinalIgnoreCase);

        foreach (RunAnswer answer in before)
        {
            byName[answer.Name] = answer;
        }

        foreach (InputAnswer? answer in answers ?? [])
        {
            if (answer?.Name is { } name && Input(definition, name.Trim()) is { Kind: not InputKind.Account } input && IsAnswered(input, answer))
            {
                byName[input.Name] = new RunAnswer(input.Name, Spelled(input, answer.Value!), StoredText.Bound(answeredBy, 256), atMachine, now);
            }
        }

        return
        [
            .. (definition.Inputs ?? [])
                .OfType<InputDeclaration>()
                .Select(input => byName.GetValueOrDefault(input.Name))
                .OfType<RunAnswer>(),
        ];
    }

    // The inputs these answers answer, as the sequence names them, Account inputs included.
    public static IReadOnlyList<string> Answered(SequenceDefinition definition, IReadOnlyList<InputAnswer?>? answers)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return
        [
            .. (answers ?? [])
                .Select(answer => answer?.Name is { } name && Input(definition, name.Trim()) is { } input && IsAnswered(input, answer) ? input.Name : null)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    // Keeps the Account answers among these for the run, each with the destination the run's own copy of the sequence
    // declares. Null when all are kept; otherwise the first problem.
    public async Task<AnswerProblem?> KeepAccountsAsync(
        Deployment run,
        SequenceDefinition definition,
        IReadOnlyList<InputAnswer?>? answers,
        RunCredentialGiver giver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(definition);

        foreach (InputAnswer? answer in answers ?? [])
        {
            if (answer?.Name is not { } name || Input(definition, name.Trim()) is not { Kind: InputKind.Account } input || !IsAnswered(input, answer))
            {
                continue;
            }

            RunCredentialProblem? problem = await credentials
                .KeepAsync(run, input, answer with { Name = input.Name }, giver, cancellationToken)
                .ConfigureAwait(false);

            if (problem is not null)
            {
                return new AnswerProblem(input.Name, $"{AnswersField}.{input.Name}", problem.Message);
            }
        }

        return null;
    }

    // The values of a run on the machine from the answers it has, the rules as they are now and the defaults. Accounts
    // are the Account inputs answered for it, those in the database and those this request keeps.
    public async Task<RunValueCheck> CheckAsync(
        Machine machine,
        Deployment run,
        SequenceDefinition definition,
        DeploymentOptions deployment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(definition);

        HashSet<string> accounts = new(await credentials.AnsweredAsync(run.Id, cancellationToken).ConfigureAwait(false), StringComparer.OrdinalIgnoreCase);

        foreach (RunCredential kept in database.ChangeTracker.Entries<RunCredential>()
            .Where(entry => entry.State == EntityState.Added && entry.Entity.DeploymentId == run.Id)
            .Select(entry => entry.Entity))
        {
            accounts.Add(kept.InputName);
        }

        IReadOnlyDictionary<string, string> answers = MachineValues.Answers(RunAnswer.Read(run.Answers));
        ValueSources sources = await machineValues.SourcesAsync(machine, definition, answers, deployment, cancellationToken).ConfigureAwait(false);

        return Check(definition, ValueResolver.Resolve(sources), answers, accounts);
    }

    // What a resolution says of the run's inputs: the required ones without an answer or a default, and those the machine
    // asks that have no answer yet, with what their questions start with.
    public static RunValueCheck Check(
        SequenceDefinition definition,
        ValueResolution resolution,
        IReadOnlyDictionary<string, string> answers,
        IReadOnlySet<string> accounts)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(accounts);

        List<InputDeclaration> inputs = [.. (definition.Inputs ?? []).OfType<InputDeclaration>().Where(input => !string.IsNullOrEmpty(input.Name))];
        HashSet<string> required = new(
            resolution.Problems
                .Where(problem => problem.Message.Code == ServerMessages.ValuesInputRequired.Code)
                .Select(problem => problem.Name),
            StringComparer.OrdinalIgnoreCase);

        bool Answered(InputDeclaration input) => input.Kind == InputKind.Account
            ? accounts.Contains(input.Name)
            : answers.TryGetValue(input.Name, out string? answer) && !string.IsNullOrEmpty(answer);

        List<InputDeclaration> missing =
        [
            .. inputs.Where(input => input.Kind == InputKind.Account
                ? input.Required && !Answered(input)
                : required.Contains(input.Name)),
        ];

        return new RunValueCheck(
            resolution,
            missing,
            [.. resolution.Problems.Where(problem => !(problem.Message.Code == ServerMessages.ValuesInputRequired.Code && required.Contains(problem.Name)))],
            [
                .. inputs
                    .Where(input => input.AskAt is InputAsk.Machine or InputAsk.Both && !Answered(input))
                    .Select(input => Asked(input, resolution)),
            ]);
    }

    // The input as the machine or the web asks it, starting with what the run would use without an answer.
    public static AgentInput Asked(InputDeclaration input, ValueResolution? resolution)
    {
        ArgumentNullException.ThrowIfNull(input);

        string? prefill = input.Kind == InputKind.Account
            ? null
            : resolution?.InputDefaults.FirstOrDefault(value => string.Equals(value.Name, input.Name, StringComparison.OrdinalIgnoreCase))?.Value
                ?? input.Default;

        return new AgentInput(
            input.Name,
            input.Label,
            input.Help,
            input.Kind,
            [.. (input.Choices ?? []).OfType<InputChoice>()],
            prefill,
            input.Required,
            input.MaxLength,
            input.Kind == InputKind.Account ? AccountRules.Trimmed(input.Account?.Domain) : null);
    }

    // The values the run started with, by name, as the agent and templates use them: each value that was used, never one
    // it overrode. Null before the run started.
    public static IReadOnlyDictionary<string, string>? Effective(Deployment run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (Read(run.Values) is not { } values)
        {
            return null;
        }

        Dictionary<string, string> effective = new(StringComparer.OrdinalIgnoreCase);

        foreach (ResolvedValue value in values)
        {
            if (!value.Overridden && value.Value is not null)
            {
                effective.TryAdd(value.Name, value.Value);
            }
        }

        return effective;
    }

    public static IReadOnlyList<ResolvedValue>? Read(string? values) =>
        values is null ? null : JsonSerializer.Deserialize(values, DdtJsonContext.Default.IReadOnlyListResolvedValue);

    public static string Write(IReadOnlyList<ResolvedValue> values) =>
        JsonSerializer.Serialize(values, DdtJsonContext.Default.IReadOnlyListResolvedValue);

    private static InputDeclaration? Input(SequenceDefinition definition, string name) =>
        (definition.Inputs ?? []).FirstOrDefault(input => input is not null && string.Equals(input.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool IsAnswered(InputDeclaration input, InputAnswer answer) => input.Kind == InputKind.Account
        ? !string.IsNullOrWhiteSpace(answer.UserName) || !string.IsNullOrEmpty(answer.Password)
        : !string.IsNullOrWhiteSpace(answer.Value);

    private static ServerMessage? ValueProblem(InputDeclaration input, string value)
    {
        int max = input.MaxLength is { } length && input.Kind == InputKind.Text
            ? Math.Min(length, SequenceValidator.MaxAnswerLength)
            : SequenceValidator.MaxAnswerLength;

        if (value.Length > max || value.Contains('\0', StringComparison.Ordinal))
        {
            return ServerMessages.DeploymentAnswerTooLong.With("label", input.Label, "max", max);
        }

        return input.Kind switch
        {
            InputKind.Choice when Choice(input, value) is null =>
                ServerMessages.DeploymentAnswerNotAChoice.With("label", input.Label, "value", value.Trim()),
            InputKind.MultiChoice when Parts(value).FirstOrDefault(part => Choice(input, part) is null) is { } other =>
                ServerMessages.DeploymentAnswerNotAChoice.With("label", input.Label, "value", other),
            InputKind.YesNo when YesNo(value) is null => ServerMessages.DeploymentAnswerYesNo.With("label", input.Label),
            _ => null,
        };
    }

    private static string Spelled(InputDeclaration input, string value) => input.Kind switch
    {
        InputKind.Choice => Choice(input, value) ?? value.Trim(),
        InputKind.MultiChoice => string.Join(';', Parts(value).Select(part => Choice(input, part) ?? part).Distinct(StringComparer.OrdinalIgnoreCase)),
        InputKind.YesNo => YesNo(value) ?? value.Trim(),
        _ => value.Trim(),
    };

    private static string? Choice(InputDeclaration input, string value) =>
        (input.Choices ?? []).FirstOrDefault(choice => choice is not null && string.Equals(choice.Value, value.Trim(), StringComparison.OrdinalIgnoreCase))?.Value;

    private static IEnumerable<string> Parts(string value) => value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string? YesNo(string value) => value.Trim().ToUpperInvariant() switch
    {
        "TRUE" or "YES" => "true",
        "FALSE" or "NO" => "false",
        _ => null,
    };
}

// Name is the input the problem is about, Field the request's field, such as answers.Owner.
public sealed record AnswerProblem(string Name, string Field, ServerMessage Message);

// What a run's values are as they stand. Missing are the required inputs that have neither an answer nor a default, and
// Problems the rest of what keeps the run from starting. AskedAtMachine are the inputs the machine asks that have no
// answer yet, required or not, as the machine asks them.
public sealed record RunValueCheck(
    ValueResolution Resolution,
    IReadOnlyList<InputDeclaration> Missing,
    IReadOnlyList<ValueProblem> Problems,
    IReadOnlyList<AgentInput> AskedAtMachine);

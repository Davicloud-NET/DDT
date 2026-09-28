// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// A sequence's inputs as the console at the machine asks them, after the pick or while a run waits at its start, and
// the answers it gives as the server takes them. The agent checks what the console can get wrong before it sends
// anything; the server checks again and has the last word. An answer is never logged, and neither is anything made from
// one: the words here name the input, never what was typed.
public static class InputQuestions
{
    public const string YesAnswer = "true";
    public const string NoAnswer = "false";

    // An Account input names the domain its account is for as the server gave it, or else as the sequence's declaration
    // in definition does.
    public static ConsoleInput ToConsole(AgentInput input, string? error, SequenceDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        string? domain = input.Kind != InputKind.Account
            ? null
            : input.Domain ?? definition?.Inputs?.FirstOrDefault(declared => declared is not null && string.Equals(declared.Name, input.Name, StringComparison.OrdinalIgnoreCase))?.Account?.Domain;

        return new ConsoleInput(
            input.Name,
            input.Label,
            input.Help,
            input.Kind switch
            {
                InputKind.Choice => ConsoleInputKind.Choice,
                InputKind.MultiChoice => ConsoleInputKind.MultiChoice,
                InputKind.YesNo => ConsoleInputKind.YesNo,
                InputKind.Account => ConsoleInputKind.Account,
                _ => ConsoleInputKind.Text,
            },
            [.. (input.Choices ?? []).Select(choice => new ConsoleChoice(choice.Value, choice.Label))],
            input.Default,
            input.Required,
            input.MaxLength,
            error,
            domain);
    }

    // What is wrong with each answer, by the input's name; empty when nothing is. An input the console gave no answer
    // for counts as unanswered.
    public static IReadOnlyDictionary<string, string> Check(IReadOnlyList<AgentInput> inputs, IReadOnlyList<ConsoleInputValue> values)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(values);

        Dictionary<string, string> problems = new(StringComparer.OrdinalIgnoreCase);

        foreach (AgentInput input in inputs)
        {
            ConsoleInputValue? value = ValueOf(values, input.Name);

            if (Problem(input, value) is { } problem)
            {
                problems[input.Name] = problem;
            }
        }

        return problems;
    }

    // The answers as the server takes them, one per input, in the order of the inputs.
    public static IReadOnlyList<InputAnswer> Answers(IReadOnlyList<AgentInput> inputs, IReadOnlyList<ConsoleInputValue> values)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(values);

        return
        [
            .. inputs.Select(input => ValueOf(values, input.Name) is { } value
                ? input.Kind == InputKind.Account
                    ? new InputAnswer(input.Name, null, value.UserName?.Trim(), value.Password)
                    : new InputAnswer(input.Name, Blank(value.Value) ? null : value.Value!.Trim())
                : new InputAnswer(input.Name, null)),
        ];
    }

    // The server's refusal names a field by the input's name, alone or at the end of a path such as answers.Office or
    // answers[Office]. Null when it names none of them.
    public static IReadOnlyDictionary<string, string>? FieldErrors(IReadOnlyList<AgentInput> inputs, IReadOnlyDictionary<string, string>? errors)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        Dictionary<string, string> matched = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string field, string message) in errors ?? new Dictionary<string, string>())
        {
            if (inputs.FirstOrDefault(input => Names(field, input.Name)) is { } named)
            {
                matched[named.Name] = message;
            }
        }

        return matched.Count > 0 ? matched : null;
    }

    private static bool Names(string field, string name) =>
        string.Equals(field, name, StringComparison.OrdinalIgnoreCase)
        || field.EndsWith($".{name}", StringComparison.OrdinalIgnoreCase)
        || field.EndsWith($"[{name}]", StringComparison.OrdinalIgnoreCase);

    private static ConsoleInputValue? ValueOf(IReadOnlyList<ConsoleInputValue> values, string name) =>
        values.FirstOrDefault(value => value is not null && string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string? Problem(AgentInput input, ConsoleInputValue? value)
    {
        if (input.Kind == InputKind.Account)
        {
            bool given = !Blank(value?.UserName) || !string.IsNullOrEmpty(value?.Password);

            return (input.Required || given) && (Blank(value?.UserName) || string.IsNullOrEmpty(value?.Password))
                ? $"{input.Label} needs a user name and a password."
                : null;
        }

        string text = value?.Value?.Trim() ?? "";

        if (text.Length == 0)
        {
            return input.Required ? $"{input.Label} needs an answer." : null;
        }

        HashSet<string> choices = [.. (input.Choices ?? []).Select(choice => choice.Value)];

        return input.Kind switch
        {
            InputKind.Text when input.MaxLength is { } maxLength && text.Length > maxLength => $"{input.Label} takes at most {maxLength} characters.",
            InputKind.Choice when !choices.Contains(text) => $"Choose one of the choices shown for {input.Label}.",
            InputKind.MultiChoice when text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(item => !choices.Contains(item)) =>
                $"Choose only choices shown for {input.Label}.",
            InputKind.YesNo when text is not (YesAnswer or NoAnswer) => $"Answer {input.Label} with yes or no.",
            _ => null,
        };
    }

    private static bool Blank(string? text) => string.IsNullOrWhiteSpace(text);
}

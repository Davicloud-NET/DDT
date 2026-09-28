// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// A sequence's inputs at the text console, a prompt for each field in order. Choices are typed as numbers, which only
// this console has, so it checks them itself; the agent checks the answers again, and asks again with what was wrong.
internal sealed class TextConsoleInputs(ISignInPrompt prompt, AgentLog log)
{
    public async Task<ConsoleAnswer?> AskAsync(InputsQuestion question, CancellationToken cancellationToken)
    {
        log.Information($"{question.SequenceName} asks these before it starts.");

        if (!string.IsNullOrWhiteSpace(question.Error))
        {
            log.Warning(question.Error);
        }

        List<ConsoleInputValue> values = [];

        foreach (ConsoleInput input in question.Inputs)
        {
            log.Information(string.IsNullOrWhiteSpace(input.Help) ? $"{input.Label}:" : $"{input.Label}: {input.Help}");

            if (!string.IsNullOrWhiteSpace(input.Error))
            {
                log.Warning(input.Error);
            }

            ConsoleInputValue? value = input.Kind switch
            {
                ConsoleInputKind.Choice => await ChoiceAsync(input, cancellationToken).ConfigureAwait(false),
                ConsoleInputKind.MultiChoice => await ChoicesAsync(input, cancellationToken).ConfigureAwait(false),
                ConsoleInputKind.YesNo => await YesNoAsync(input, cancellationToken).ConfigureAwait(false),
                ConsoleInputKind.Account => await AccountAsync(input, cancellationToken).ConfigureAwait(false),
                _ => await TextAsync(input, cancellationToken).ConfigureAwait(false),
            };

            if (value is null)
            {
                return null;
            }

            values.Add(value);
        }

        return new ConsoleAnswer(Values: values);
    }

    // Enter alone keeps the default, which the prompt shows.
    private async Task<ConsoleInputValue?> TextAsync(ConsoleInput input, CancellationToken cancellationToken)
    {
        string label = string.IsNullOrEmpty(input.Default) ? input.Label : $"{input.Label} [{input.Default}]";

        while (true)
        {
            if (await prompt.ReadLineAsync(label, secret: false, cancellationToken).ConfigureAwait(false) is not { } typed)
            {
                return null;
            }

            string value = typed.Trim().Length == 0 ? input.Default ?? string.Empty : typed.Trim();

            if (input.Required && value.Length == 0)
            {
                log.Warning($"{input.Label} needs an answer.");
            }
            else if (input.MaxLength is { } maxLength && value.Length > maxLength)
            {
                log.Warning($"{input.Label} takes at most {maxLength} characters.");
            }
            else
            {
                return new ConsoleInputValue(input.Name, value);
            }
        }
    }

    // The list comes again before every attempt, as with the sequences.
    private async Task<ConsoleInputValue?> ChoiceAsync(ConsoleInput input, CancellationToken cancellationToken)
    {
        while (true)
        {
            ListChoices(input);

            if (await prompt.ReadLineAsync(NumberLabel(input, "Number"), secret: false, cancellationToken).ConfigureAwait(false) is not { } typed)
            {
                return null;
            }

            if (typed.Trim().Length == 0 && Chosen(input, input.Default) is { } kept)
            {
                return new ConsoleInputValue(input.Name, kept.Value);
            }

            if (typed.Trim().Length == 0 && !input.Required)
            {
                return new ConsoleInputValue(input.Name, null);
            }

            if (ChoiceNumber(input, typed) is { } chosen)
            {
                return new ConsoleInputValue(input.Name, input.Choices[chosen - 1].Value);
            }

            log.Warning($"Type a number from 1 to {input.Choices.Count}.");
        }
    }

    // Several numbers, separated by commas or spaces. The answer names the values in the order of the list.
    private async Task<ConsoleInputValue?> ChoicesAsync(ConsoleInput input, CancellationToken cancellationToken)
    {
        while (true)
        {
            ListChoices(input);

            if (await prompt.ReadLineAsync(NumberLabel(input, "Numbers, separated by commas"), secret: false, cancellationToken)
                .ConfigureAwait(false) is not { } typed)
            {
                return null;
            }

            string[] parts = typed.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0 && !string.IsNullOrEmpty(input.Default))
            {
                return new ConsoleInputValue(input.Name, input.Default);
            }

            if (parts.Length == 0 && !input.Required)
            {
                return new ConsoleInputValue(input.Name, string.Empty);
            }

            int?[] numbers = [.. parts.Select(part => ChoiceNumber(input, part))];

            if (parts.Length > 0 && numbers.All(number => number is not null))
            {
                HashSet<int> chosen = [.. numbers.Select(number => number!.Value)];
                IEnumerable<string> values = input.Choices.Where((_, index) => chosen.Contains(index + 1)).Select(choice => choice.Value);

                return new ConsoleInputValue(input.Name, string.Join(';', values));
            }

            log.Warning($"Type numbers from 1 to {input.Choices.Count}, separated by commas.");
        }
    }

    private async Task<ConsoleInputValue?> YesNoAsync(ConsoleInput input, CancellationToken cancellationToken)
    {
        bool? kept = bool.TryParse(input.Default, out bool yes) ? yes : null;
        string label = kept switch
        {
            true => $"{input.Label} (y/n) [y]",
            false => $"{input.Label} (y/n) [n]",
            _ => $"{input.Label} (y/n)",
        };

        while (true)
        {
            if (await prompt.ReadLineAsync(label, secret: false, cancellationToken).ConfigureAwait(false) is not { } typed)
            {
                return null;
            }

            bool? answer = typed.Trim().ToUpperInvariant() switch
            {
                "Y" or "YES" => true,
                "N" or "NO" => false,
                "" => kept,
                _ => null,
            };

            if (answer is { } said)
            {
                return new ConsoleInputValue(input.Name, said ? "true" : "false");
            }

            if (typed.Trim().Length == 0 && !input.Required)
            {
                return new ConsoleInputValue(input.Name, null);
            }

            log.Warning("Type y or n.");
        }
    }

    // A user name, then its password where nobody sees it. An empty password goes back to the user name, as at the
    // sign-in; an empty user name leaves an input that is not required unanswered.
    private async Task<ConsoleInputValue?> AccountAsync(ConsoleInput input, CancellationToken cancellationToken)
    {
        string label = string.IsNullOrEmpty(input.Default) ? "User name" : $"User name [{input.Default}]";

        log.Information(string.IsNullOrWhiteSpace(input.Domain)
            ? "DDT keeps the password for this run only and never shows it."
            : $"For {input.Domain}. DDT keeps the password for this run only and never shows it.");

        while (true)
        {
            if (await prompt.ReadLineAsync(label, secret: false, cancellationToken).ConfigureAwait(false) is not { } typed)
            {
                return null;
            }

            string userName = typed.Trim().Length == 0 ? input.Default ?? string.Empty : typed.Trim();

            if (userName.Length == 0)
            {
                if (!input.Required)
                {
                    return new ConsoleInputValue(input.Name, null);
                }

                log.Warning($"{input.Label} needs a user name.");

                continue;
            }

            if (await prompt.ReadLineAsync($"Password for {userName}", secret: true, cancellationToken).ConfigureAwait(false) is not { } password)
            {
                return null;
            }

            if (password.Length > 0)
            {
                return new ConsoleInputValue(input.Name, null, userName, password);
            }
        }
    }

    private void ListChoices(ConsoleInput input)
    {
        for (int number = 1; number <= input.Choices.Count; number++)
        {
            ConsoleChoice choice = input.Choices[number - 1];
            string text = choice.Label ?? choice.Value;
            log.Information(IsDefault(input, choice) ? $"  {number}. {text} (default)" : $"  {number}. {text}");
        }
    }

    // Enter alone keeps the default, where there is one.
    private static string NumberLabel(ConsoleInput input, string label) =>
        string.IsNullOrEmpty(input.Default) ? label : $"{label} [Enter keeps the default]";

    private static bool IsDefault(ConsoleInput input, ConsoleChoice choice) =>
        input.Kind == ConsoleInputKind.MultiChoice
            ? input.Default?.Split(';').Contains(choice.Value, StringComparer.Ordinal) == true
            : string.Equals(input.Default, choice.Value, StringComparison.Ordinal);

    private static ConsoleChoice? Chosen(ConsoleInput input, string? value) =>
        input.Choices.FirstOrDefault(choice => string.Equals(choice.Value, value, StringComparison.Ordinal));

    // The number of a choice shown, from 1, or null.
    private static int? ChoiceNumber(ConsoleInput input, string typed) =>
        int.TryParse(typed.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= 1 && number <= input.Choices.Count
            ? number
            : null;
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;
using DDT.ConsoleProtocol;
using DDT.Core.Boot;

namespace DDT.Agent.Consoles;

// The console Windows PE opens for the agent, and the one the graphical console falls back to. The state shows as the
// lines the agent logs as it goes, and a question is asked a line at a time through the prompt, with the list or the
// warning it needs in the log first, so the machine's log keeps them. Choices are typed as numbers, which only this
// console has, so it checks them itself. What the person got wrong at the question before, such as a wrong password,
// is on the console already, as the warning the agent logged; a sequence's inputs say it field by field, before each
// prompt. What is typed is never logged, only which field it answers.
public sealed class TextMachineConsole(ISignInPrompt prompt, AgentLog log) : IMachineConsole
{
    public bool CanAsk => prompt.IsAvailable;

    // Every change has its line in the log.
    public void Show(ConsoleState state)
    {
    }

    // AgentLog writes each line to this console itself.
    public void Write(ConsoleLogLine line)
    {
    }

    public Task<ConsoleAnswer?> AskAsync(ConsoleQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        return question switch
        {
            SignInQuestion signIn => SignInAsync(signIn, cancellationToken),
            SequenceQuestion sequence => SequenceAsync(sequence, cancellationToken),
            DiskQuestion disk => DiskAsync(disk, cancellationToken),
            ComputerNameQuestion computerName => ComputerNameAsync(computerName, cancellationToken),
            EraseQuestion erase => EraseAsync(erase, cancellationToken),
            SecureBootQuestion secureBoot => SecureBootAsync(secureBoot, cancellationToken),
            InputsQuestion inputs => InputsAsync(inputs, cancellationToken),
            PauseQuestion pause => PauseAsync(pause, cancellationToken),
            _ => throw new ArgumentException("The text console does not know this question.", nameof(question)),
        };
    }

    private async Task<ConsoleAnswer?> SignInAsync(SignInQuestion question, CancellationToken cancellationToken)
    {
        (string label, bool secret) = question.Field switch
        {
            SignInField.UserName => ("User name", false),
            SignInField.Password => ($"Password for {question.UserName}", true),
            _ => ("Authenticator code", false),
        };

        return Typed(await prompt.ReadLineAsync(label, secret, cancellationToken).ConfigureAwait(false));
    }

    // The list comes again before every attempt, as a wrong number may have scrolled it away.
    private async Task<ConsoleAnswer?> SequenceAsync(SequenceQuestion question, CancellationToken cancellationToken)
    {
        IReadOnlyList<SequenceOption> sequences = question.Sequences;

        while (true)
        {
            log.Information("Task sequences this machine can run:");

            for (int number = 1; number <= sequences.Count; number++)
            {
                log.Information($"  {number}. {Describe(sequences[number - 1])}");
            }

            if (await prompt.ReadLineAsync("Sequence number", secret: false, cancellationToken).ConfigureAwait(false) is not { } typed)
            {
                return null;
            }

            if (int.TryParse(typed.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int chosen)
                && chosen >= 1
                && chosen <= sequences.Count)
            {
                return new ConsoleAnswer(SequenceId: sequences[chosen - 1].Id);
            }

            log.Warning($"Type a number from 1 to {sequences.Count}.");
        }
    }

    private async Task<ConsoleAnswer?> DiskAsync(DiskQuestion question, CancellationToken cancellationToken)
    {
        while (true)
        {
            log.Information("Disks DDT can install on:");

            foreach (ConsoleDisk disk in question.Disks)
            {
                log.Information($"  {LocalDisk.Describe(disk.Number, disk.Model, disk.SizeBytes, disk.BusType, disk.PartitionCount)}");
            }

            if (await prompt.ReadLineAsync("Disk number", secret: false, cancellationToken).ConfigureAwait(false) is not { } typed)
            {
                return null;
            }

            if (int.TryParse(typed.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                && question.Disks.Any(disk => disk.Number == number))
            {
                return new ConsoleAnswer(DiskNumber: number);
            }

            log.Warning($"Type one of the disk numbers shown: {string.Join(", ", question.Disks.Select(disk => disk.Number))}.");
        }
    }

    private async Task<ConsoleAnswer?> ComputerNameAsync(ComputerNameQuestion question, CancellationToken cancellationToken)
    {
        log.Information($"{question.SequenceName} needs a computer name for this machine.");

        return Typed(await prompt.ReadLineAsync("Computer name", secret: false, cancellationToken).ConfigureAwait(false));
    }

    private async Task<ConsoleAnswer?> EraseAsync(EraseQuestion question, CancellationToken cancellationToken)
    {
        ConsoleDisk disk = question.Disk;
        log.Warning(
            $"All data on disk {disk.Number} ({LocalDisk.DisplayModelOf(disk.Model)}, {ByteSize.Format(disk.SizeBytes)}, " +
            $"{LocalDisk.Partitions(disk.PartitionCount)}) will be erased by {question.SequenceName}.");

        return Typed(await prompt.ReadLineAsync($"Type {question.Word} to continue", secret: false, cancellationToken)
            .ConfigureAwait(false));
    }

    private async Task<ConsoleAnswer?> SecureBootAsync(SecureBootQuestion question, CancellationToken cancellationToken)
    {
        string signedUnder = MicrosoftUefiCa.Describe(ConsoleValues.ToUefiCa(question.SignedUnder));
        log.Warning(question.Problem == SecureBootProblem.UntrustedCa
            ? $"{question.ImageName} is signed under {signedUnder}, which this machine's firmware does not trust. It starts only " +
                "once that CA is allowed or Secure Boot is turned off in the firmware setup."
            : $"{question.ImageName} {(question.Problem == SecureBootProblem.NotSigned ? "will" : "may")} not start with Secure " +
                "Boot on, and this machine has Secure Boot on. It starts only once Secure Boot is turned off in the firmware setup, " +
                "or your own key is enrolled.");

        return Typed(await prompt.ReadLineAsync($"Type {question.Word} to write it all the same", secret: false, cancellationToken)
            .ConfigureAwait(false));
    }

    // One prompt for each field, in order. The agent checks the answers again, and asks again with what was wrong.
    private async Task<ConsoleAnswer?> InputsAsync(InputsQuestion question, CancellationToken cancellationToken)
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

    // A Pause step waits for Enter. The agent withdraws the prompt when the run was continued on the web.
    private async Task<ConsoleAnswer?> PauseAsync(PauseQuestion question, CancellationToken cancellationToken)
    {
        log.Warning($"{question.StepName} pauses the run.");

        foreach (string line in question.Message.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            log.Information(line);
        }

        log.Information("When it is done, press Enter. An operator can also let the run go on from the web.");

        return await prompt.ReadLineAsync("Press Enter to go on", secret: false, cancellationToken).ConfigureAwait(false) is null
            ? null
            : new ConsoleAnswer(Continue: true);
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

    private static ConsoleAnswer? Typed(string? typed) => typed is null ? null : new ConsoleAnswer(Text: typed);

    private static string Describe(SequenceOption sequence)
    {
        IEnumerable<string> details = new[]
        {
            sequence.Suggested ? "suggested for this machine" : null,
            sequence.ErasesDisk ? "erases a disk" : null,
            sequence.NotSignedForSecureBoot ? "not for Secure Boot" : null,
            sequence.NotTrustedHere ? "not for this machine's Secure Boot" : null,
            sequence.RequiredBytes > 0 ? $"needs {ByteSize.Format(sequence.RequiredBytes)}" : null,
        }.OfType<string>();

        string text = details.Any() ? $"{sequence.Name} ({string.Join(", ", details)})" : sequence.Name;

        return string.IsNullOrWhiteSpace(sequence.Description) ? text : $"{text}: {sequence.Description}";
    }
}

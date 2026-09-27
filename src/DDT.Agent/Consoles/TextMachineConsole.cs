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
// is on the console already, as the warning the agent logged.
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

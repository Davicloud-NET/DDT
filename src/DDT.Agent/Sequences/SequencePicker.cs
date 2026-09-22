// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Core.Unattend;

namespace DDT.Agent.Sequences;

// What the technician signed in at the machine has chosen so far: a sequence, then only what that sequence needs: a
// disk when it erases one and there are several, a computer name when the server needs one, and ERASE before a disk
// is erased. Anything but ERASE at that question goes back to the list, so nothing is erased by a stray key. The
// sequences an assignment rule suggests for this machine come first.
public sealed class SequencePicker(ISignInPrompt prompt, AgentLog log)
{
    public const string ConfirmationWord = "ERASE";

    public const string ChangedAfterChoiceMessage =
        "The sequence was changed after it was chosen at this machine and now erases a disk, which nobody confirmed with " +
        "ERASE, so it does not run and nothing was erased. Choose it again.";

    private IReadOnlyList<AgentSequenceChoice> _sequences = [];
    private IReadOnlyList<LocalDisk> _disks = [];
    private PickerQuestion _question = PickerQuestion.None;
    private AgentSequenceChoice? _sequence;
    private LocalDisk? _disk;
    private string? _computerName;

    public bool IsAvailable => prompt.IsAvailable;

    // True once there is something to choose from.
    public bool IsOffered => _question != PickerQuestion.None;

    // The disk chosen so far, which is the confirmed one once Accept returns a request for a sequence that erases it.
    public LocalDisk? ChosenDisk => _disk;

    // disks may be empty when no sequence erases a disk.
    public void Offer(IReadOnlyList<AgentSequenceChoice> sequences, IReadOnlyList<LocalDisk> disks)
    {
        ArgumentNullException.ThrowIfNull(sequences);
        ArgumentNullException.ThrowIfNull(disks);

        AgentSequenceChoice[] runnable = [.. sequences.Where(sequence => !sequence.ErasesDisk || disks.Count > 0)];

        if (runnable.Length == 0)
        {
            Reset();

            return;
        }

        _sequences = [.. runnable.Where(sequence => sequence.Suggested), .. runnable.Where(sequence => !sequence.Suggested)];
        _disks = disks;
        StartOver();
    }

    public void Reset()
    {
        _sequences = [];
        _disks = [];
        _question = PickerQuestion.None;
        _sequence = null;
        _disk = null;
        _computerName = null;
    }

    public Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        switch (_question)
        {
            case PickerQuestion.Sequence:
                log.Information("Task sequences this machine can run:");

                for (int number = 1; number <= _sequences.Count; number++)
                {
                    log.Information($"  {number}. {Describe(_sequences[number - 1])}");
                }

                return prompt.ReadLineAsync("Sequence number", secret: false, cancellationToken);
            case PickerQuestion.Disk:
                log.Information("Disks DDT can install on:");

                foreach (LocalDisk disk in _disks)
                {
                    log.Information($"  {disk.Describe()}");
                }

                return prompt.ReadLineAsync("Disk number", secret: false, cancellationToken);
            case PickerQuestion.ComputerName:
                log.Information($"{_sequence!.Name} needs a computer name for this machine.");

                return prompt.ReadLineAsync("Computer name", secret: false, cancellationToken);
            case PickerQuestion.Confirmation:
                log.Warning(
                    $"All data on disk {_disk!.Number} ({_disk.DisplayModel}, {ByteSize.Format(_disk.SizeBytes)}, " +
                    $"{LocalDisk.Partitions(_disk.PartitionCount)}) will be erased by {_sequence!.Name}.");

                return prompt.ReadLineAsync($"Type {ConfirmationWord} to continue", secret: false, cancellationToken);
            default:
                throw new InvalidOperationException("There is nothing to pick yet.");
        }
    }

    // The request to send once the technician has answered everything the sequence needs, otherwise null.
    public AgentRunRequest? Accept(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);

        string answer = typed.Trim();

        switch (_question)
        {
            case PickerQuestion.Sequence:
                if (!int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1 || number > _sequences.Count)
                {
                    log.Warning($"Type a number from 1 to {_sequences.Count}.");

                    return null;
                }

                _sequence = _sequences[number - 1];
                _disk = _sequence.ErasesDisk && _disks.Count == 1 ? _disks[0] : null;

                return Ask(_sequence.ErasesDisk && _disk is null ? PickerQuestion.Disk : AfterDisk());
            case PickerQuestion.Disk:
                LocalDisk? disk = int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out int diskNumber)
                    ? _disks.FirstOrDefault(candidate => candidate.Number == diskNumber)
                    : null;

                if (disk is null)
                {
                    log.Warning($"Type one of the disk numbers shown: {string.Join(", ", _disks.Select(candidate => candidate.Number))}.");

                    return null;
                }

                _disk = disk;

                return Ask(AfterDisk());
            case PickerQuestion.ComputerName:
                if (!ComputerNames.IsValid(answer, out string error))
                {
                    log.Warning(error);

                    return null;
                }

                _computerName = answer;

                return Ask(AfterComputerName());
            case PickerQuestion.Confirmation:
                if (!string.Equals(answer, ConfirmationWord, StringComparison.Ordinal))
                {
                    log.Information("Nothing was erased.");
                    StartOver();

                    return null;
                }

                return Request();
            default:
                return null;
        }
    }

    // What runs is the server's copy of the sequence as it was when it was chosen, which an administrator may have
    // changed while the list was shown.
    public void Picked(AgentRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (_disk is null && run.Sequence.Steps.Any(step => step.ErasesDisk))
        {
            log.Warning(ChangedAfterChoiceMessage);
        }
        else
        {
            log.Information(_disk is { } disk ? $"{run.SequenceName} is going to run on disk {disk.Number}." : $"{run.SequenceName} is going to run.");
        }

        Reset();
    }

    // The server would not take the choice; what it says decides whether offering again makes sense, so the picker
    // starts from a fresh list.
    public void Refused(string reason)
    {
        log.Warning($"The server did not accept the choice: {reason}");
        Reset();
    }

    // Nothing reached the server, or nothing came back: the last question is asked again.
    public void NotSent(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        log.Warning($"Cannot send the choice to the server ({exception.Message}). Try again.");
    }

    private void StartOver()
    {
        _question = PickerQuestion.Sequence;
        _sequence = null;
        _disk = null;
        _computerName = null;
    }

    private PickerQuestion AfterDisk() => _sequence!.NeedsComputerName ? PickerQuestion.ComputerName : AfterComputerName();

    private PickerQuestion AfterComputerName() => _sequence!.ErasesDisk ? PickerQuestion.Confirmation : PickerQuestion.None;

    // A sequence that has nothing more to ask starts at once.
    private AgentRunRequest? Ask(PickerQuestion question)
    {
        if (question == PickerQuestion.None)
        {
            return Request();
        }

        _question = question;

        return null;
    }

    private AgentRunRequest Request() => new(_sequence!.Id, _sequence.ErasesDisk ? _disk!.Number : null, _computerName);

    private static string Describe(AgentSequenceChoice sequence)
    {
        IEnumerable<string> details = new[]
        {
            sequence.Suggested ? "suggested for this machine" : null,
            sequence.ErasesDisk ? "erases a disk" : null,
            sequence.RequiredBytes > 0 ? $"needs {ByteSize.Format(sequence.RequiredBytes)}" : null,
        }.OfType<string>();

        string text = details.Any() ? $"{sequence.Name} ({string.Join(", ", details)})" : sequence.Name;

        return string.IsNullOrWhiteSpace(sequence.Description) ? text : $"{text}: {sequence.Description}";
    }
}

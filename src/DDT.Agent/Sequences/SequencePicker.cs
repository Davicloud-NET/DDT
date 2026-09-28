// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Contracts.Sequences;
using DDT.Core.Boot;
using DDT.Core.Unattend;

namespace DDT.Agent.Sequences;

// What the technician signed in at the machine has chosen so far: a sequence, then only what that sequence needs: a
// disk when it erases one and there are several, a computer name when the server needs one, the sequence's inputs asked
// at the machine, ERASE before a disk is erased, and ANYWAY before a disk image is written that will not start with the
// Secure Boot the machine has on. Anything but the word at those questions goes back to the list, so nothing is erased
// by a stray key, and so does Back at any question after the list. The sequences an assignment rule suggests for this
// machine come first. The answers to the inputs, passwords among them, stay here until they go with the choice, and are
// forgotten when it is sent or the picker starts over.
public sealed class SequencePicker(IMachineConsole console, AgentLog log)
{
    public const string ConfirmationWord = "ERASE";

    public const string SecureBootWord = "ANYWAY";

    public const string ChangedAfterChoiceMessage =
        "The sequence was changed after it was chosen at this machine and now erases a disk, which nobody confirmed with " +
        "ERASE, so it does not run and nothing was erased. Choose it again.";

    private IReadOnlyList<AgentSequenceChoice> _sequences = [];
    private IReadOnlyList<LocalDisk> _disks = [];
    private PickerQuestion _question = PickerQuestion.None;
    private AgentSequenceChoice? _sequence;
    private LocalDisk? _disk;
    private string? _computerName;
    private string? _computerNameError;
    private IReadOnlyList<ConsoleInputValue>? _answers;
    private IReadOnlyDictionary<string, string> _inputErrors = new Dictionary<string, string>();
    private string? _inputsError;
    private bool? _secureBootEnabled;
    private UefiCa? _trustedUefiCas;
    private bool _allowSecureBootMismatch;

    public bool IsAvailable => console.CanAsk;

    // True once there is something to choose from.
    public bool IsOffered => _question != PickerQuestion.None;

    // The disk chosen so far, which is the confirmed one once Accept returns a request for a sequence that erases it.
    public LocalDisk? ChosenDisk => _disk;

    // disks may be empty when no sequence erases a disk. secureBootEnabled is what the firmware says, if anything, and
    // trustedUefiCas which of Microsoft's third-party UEFI CAs it trusts.
    public void Offer(
        IReadOnlyList<AgentSequenceChoice> sequences,
        IReadOnlyList<LocalDisk> disks,
        bool? secureBootEnabled = null,
        UefiCa? trustedUefiCas = null)
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
        _secureBootEnabled = secureBootEnabled;
        _trustedUefiCas = trustedUefiCas;
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
        _computerNameError = null;
        ForgetAnswers();
        _secureBootEnabled = null;
        _trustedUefiCas = null;
        _allowSecureBootMismatch = false;
    }

    public Task<ConsoleAnswer?> ReadAsync(CancellationToken cancellationToken) => _question switch
    {
        PickerQuestion.Sequence => console.AskAsync(new SequenceQuestion([.. _sequences.Select(Option)]), cancellationToken),
        PickerQuestion.Disk => console.AskAsync(
            new DiskQuestion(_sequence!.Name, [.. _disks.Select(disk => disk.ToConsoleDisk())]),
            cancellationToken),
        PickerQuestion.ComputerName => console.AskAsync(
            new ComputerNameQuestion(_sequence!.Name, ComputerNames.MaxLength, _computerNameError),
            cancellationToken),
        PickerQuestion.Inputs => console.AskAsync(
            new InputsQuestion(
                _sequence!.Name,
                [.. Inputs(_sequence).Select(input => InputQuestions.ToConsole(input, _inputErrors.GetValueOrDefault(input.Name)))],
                _inputsError),
            cancellationToken),
        PickerQuestion.Confirmation => console.AskAsync(
            new EraseQuestion(_sequence!.Name, _disk!.ToConsoleDisk(), ConfirmationWord),
            cancellationToken),
        PickerQuestion.SecureBoot => console.AskAsync(SecureBootQuestionFor(_sequence!), cancellationToken),
        _ => throw new InvalidOperationException("There is nothing to pick yet."),
    };

    // The request to send once the technician has answered everything the sequence needs, otherwise null.
    public AgentRunRequest? Accept(ConsoleAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        string typed = answer.Text?.Trim() ?? string.Empty;

        switch (_question)
        {
            case PickerQuestion.Sequence:
                // Back has nowhere to go from the list; the text console checks the number itself.
                if (answer.Back)
                {
                    return null;
                }

                if (_sequences.FirstOrDefault(candidate => candidate.Id == answer.SequenceId) is not { } sequence)
                {
                    log.Warning("Choose one of the sequences shown.");

                    return null;
                }

                _sequence = sequence;
                _disk = _sequence.ErasesDisk && _disks.Count == 1 ? _disks[0] : null;

                return Ask(_sequence.ErasesDisk && _disk is null ? PickerQuestion.Disk : AfterDisk());
            case PickerQuestion.Disk:
                if (answer.Back)
                {
                    StartOver();

                    return null;
                }

                if (_disks.FirstOrDefault(candidate => candidate.Number == answer.DiskNumber) is not { } disk)
                {
                    log.Warning("Choose one of the disks shown.");

                    return null;
                }

                _disk = disk;

                return Ask(AfterDisk());
            case PickerQuestion.ComputerName:
                if (answer.Back)
                {
                    StartOver();

                    return null;
                }

                if (!ComputerNames.IsValid(typed, out string error))
                {
                    log.Warning(error);
                    _computerNameError = error;

                    return null;
                }

                _computerName = typed;
                _computerNameError = null;

                return Ask(AfterComputerName());
            case PickerQuestion.Inputs:
                if (answer.Back)
                {
                    StartOver();

                    return null;
                }

                // What was typed is never logged, only which inputs it did not answer well.
                IReadOnlyList<ConsoleInputValue> values = answer.Values ?? [];
                _inputErrors = InputQuestions.Check(Inputs(_sequence!), values);
                _inputsError = null;

                if (_inputErrors.Count > 0)
                {
                    log.Warning($"Answer these again: {string.Join(", ", Inputs(_sequence!).Where(input => _inputErrors.ContainsKey(input.Name)).Select(input => input.Label))}.");

                    return null;
                }

                _answers = values;

                return Ask(AfterInputs());
            case PickerQuestion.Confirmation:
                if (answer.Back || !string.Equals(typed, ConfirmationWord, StringComparison.Ordinal))
                {
                    log.Information("Nothing was erased.");
                    StartOver();

                    return null;
                }

                return Ask(AfterConfirmation());
            case PickerQuestion.SecureBoot:
                if (answer.Back || !string.Equals(typed, SecureBootWord, StringComparison.Ordinal))
                {
                    log.Information("Nothing was erased.");
                    StartOver();

                    return null;
                }

                _allowSecureBootMismatch = true;

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

        if (_disk is null && SequenceTree.Nodes(run.Sequence).Any(step => step.ErasesDisk))
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
    // starts from a fresh list. When it names answers to the sequence's inputs as what it did not take, they are asked
    // again with what was wrong at each, and the rest of the choice stands.
    public void Refused(string reason, IReadOnlyDictionary<string, string>? fieldErrors = null)
    {
        log.Warning($"The server did not accept the choice: {reason}");

        if (_sequence is { } sequence && InputQuestions.FieldErrors(Inputs(sequence), fieldErrors) is { } errors)
        {
            _question = PickerQuestion.Inputs;
            _inputErrors = errors;
            _inputsError = reason;
            _answers = null;

            return;
        }

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
        _computerNameError = null;
        ForgetAnswers();
        _allowSecureBootMismatch = false;
    }

    private void ForgetAnswers()
    {
        _answers = null;
        _inputErrors = new Dictionary<string, string>();
        _inputsError = null;
    }

    private static IReadOnlyList<AgentInput> Inputs(AgentSequenceChoice sequence) => [.. sequence.Inputs?.Where(input => input is not null) ?? []];

    private PickerQuestion AfterDisk() => _sequence!.NeedsComputerName ? PickerQuestion.ComputerName : AfterComputerName();

    private PickerQuestion AfterComputerName() => Inputs(_sequence!).Count > 0 ? PickerQuestion.Inputs : AfterInputs();

    private PickerQuestion AfterInputs() => _sequence!.ErasesDisk ? PickerQuestion.Confirmation : AfterConfirmation();

    // Only where the firmware says Secure Boot is on: elsewhere the image may well start.
    private PickerQuestion AfterConfirmation() =>
        (_sequence!.RawImageBootCapability is ImageBootCapability.NotSigned or ImageBootCapability.Unknown || UntrustedCa(_sequence))
        && _secureBootEnabled == true
            ? PickerQuestion.SecureBoot
            : PickerQuestion.None;

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

    private AgentRunRequest Request() =>
        new(_sequence!.Id, _sequence.ErasesDisk ? _disk!.Number : null, _computerName, _allowSecureBootMismatch)
        {
            Answers = _answers is { } values ? InputQuestions.Answers(Inputs(_sequence), values) : null,
        };

    // Signed for Secure Boot, but only under CAs this machine's firmware does not trust, while Secure Boot is on.
    private bool UntrustedCa(AgentSequenceChoice sequence) =>
        sequence.RawImageBootCapability == ImageBootCapability.SecureBootOk
        && _secureBootEnabled == true
        && MicrosoftUefiCa.Untrusted(_trustedUefiCas, sequence.RawImageSignedUnder);

    private SequenceOption Option(AgentSequenceChoice sequence) => new(
        sequence.Id,
        sequence.Name,
        sequence.Description,
        sequence.Suggested,
        sequence.ErasesDisk,
        sequence.NeedsComputerName,
        sequence.RequiredBytes,
        NotSignedForSecureBoot: sequence.RawImageBootCapability is ImageBootCapability.NotSigned or ImageBootCapability.Unknown,
        NotTrustedHere: UntrustedCa(sequence));

    private SecureBootQuestion SecureBootQuestionFor(AgentSequenceChoice writing) => new(
        writing.Name,
        writing.RawImageName,
        UntrustedCa(writing)
            ? SecureBootProblem.UntrustedCa
            : writing.RawImageBootCapability == ImageBootCapability.NotSigned ? SecureBootProblem.NotSigned : SecureBootProblem.MayNotStart,
        ConsoleValues.ToConsole(writing.RawImageSignedUnder),
        SecureBootWord);
}

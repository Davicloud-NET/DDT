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

// What the technician signed in at the machine has chosen so far: a sequence, then only the questions it needs, up to
// ERASE and ANYWAY. The answers to the inputs, passwords included, are forgotten once sent or when the picker starts
// over.
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

    // disks may be empty when no sequence erases a disk. secureBootEnabled is what the firmware says, if anything.
    // trustedUefiCas says which of Microsoft's third-party UEFI CAs the firmware trusts.
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
            new ComputerNameQuestion(_sequence!.Name, ComputerNames.MaxLength, _computerNameError, _sequence.ComputerName),
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

        return _question switch
        {
            PickerQuestion.Sequence => AcceptSequence(answer),
            PickerQuestion.Disk => AcceptDisk(answer),
            PickerQuestion.ComputerName => AcceptComputerName(answer, typed),
            PickerQuestion.Inputs => AcceptInputs(answer),
            PickerQuestion.Confirmation => Confirmed(answer, typed, ConfirmationWord) ? Ask(AfterConfirmation()) : null,
            PickerQuestion.SecureBoot => Confirmed(answer, typed, SecureBootWord) ? AllowSecureBootMismatch() : null,
            _ => null,
        };
    }

    // What runs is the server's copy of the sequence at the moment it was chosen. An administrator may have changed it
    // while the list was shown.
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

    // The picker starts from a fresh list, because the reason decides whether offering again makes sense. The exception
    // is when the server named answers to the inputs it didn't take. Those are asked again, and the rest of the choice
    // stands.
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

    // Nothing reached the server, or nothing came back. The last question is asked again.
    public void NotSent(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        log.Warning($"Cannot send the choice to the server ({exception.Message}). Try again.");
    }

    private AgentRunRequest? AcceptSequence(ConsoleAnswer answer)
    {
        // Back has nowhere to go from the list. The text console checks the number itself.
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
    }

    private AgentRunRequest? AcceptDisk(ConsoleAnswer answer)
    {
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
    }

    private AgentRunRequest? AcceptComputerName(ConsoleAnswer answer, string typed)
    {
        if (answer.Back)
        {
            StartOver();

            return null;
        }

        // Enter keeps the name the values give. The run then takes it from the values, so the request carries no
        // name. A name typed here wins and becomes the machine's.
        string name = typed.Length == 0 && _sequence!.ComputerName is { } given ? given : typed;

        if (!ComputerNames.IsValid(name, out string error))
        {
            log.Warning(error);
            _computerNameError = error;

            return null;
        }

        _computerName = string.Equals(name, _sequence!.ComputerName, StringComparison.OrdinalIgnoreCase) ? null : name;
        _computerNameError = null;

        return Ask(AfterComputerName());
    }

    private AgentRunRequest? AcceptInputs(ConsoleAnswer answer)
    {
        if (answer.Back)
        {
            StartOver();

            return null;
        }

        // What was typed is never logged, only which inputs weren't answered well.
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
    }

    // Anything but the word, or Back, goes back to the list, so nothing is erased by a stray key.
    private bool Confirmed(ConsoleAnswer answer, string typed, string word)
    {
        if (!answer.Back && string.Equals(typed, word, StringComparison.Ordinal))
        {
            return true;
        }

        log.Information("Nothing was erased.");
        StartOver();

        return false;
    }

    private AgentRunRequest AllowSecureBootMismatch()
    {
        _allowSecureBootMismatch = true;

        return Request();
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

    // Only when the firmware says Secure Boot is on. Otherwise the image may well start.
    private PickerQuestion AfterConfirmation() =>
        (_sequence!.RawImageBootCapability is ImageBootCapability.NotSigned or ImageBootCapability.Unknown || UntrustedCa(_sequence))
        && _secureBootEnabled == true
            ? PickerQuestion.SecureBoot
            : PickerQuestion.None;

    // A sequence that has nothing more to ask starts right away.
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

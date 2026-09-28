// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// The questions the machine asks at its console while it polls: the sign-in, and which sequence and disk to pick.
// Only reading the keyboard runs alongside polling. The loop sends the answer to the server.
internal sealed class ConsolePrompts(IAgentServer server, ConsoleStatus status, IDiskPartitioner disks, AgentLog log, AgentRegistrar registrar)
{
    private SignInConversation _conversation = new(status.Console, log);
    private SequencePicker _picker = new(status.Console, log);
    private CancellationTokenSource? _stopTyping;
    private Task<ConsoleAnswer?>? _typing;
    private bool _typingForPicker;

    // The disks the picker offers. They're read once each time the machine may pick, and are null until then.
    private IReadOnlyList<LocalDisk>? _pickableDisks;
    private bool _toldNoSequences;

    // The disk last confirmed with ERASE in this process. It's kept after the pick is answered, because a pick the
    // server stored but didn't confirm still arrives as an Assigned run.
    public LocalDisk? ConfirmedDisk { get; private set; }

    // The run last picked at this console without confirming a disk with ERASE. That run must not erase a disk.
    public Guid? PickedWithoutErase { get; set; }

    public bool CanSignIn => _conversation.IsAvailable;

    public bool CanPick => _picker.IsAvailable;

    // The line being typed, or null while nothing is asked.
    public Task<ConsoleAnswer?>? Typing => _typing;

    // Every registration starts the sign-in and the picker afresh.
    public void StartSession()
    {
        _conversation = new SignInConversation(status.Console, log);
        _picker = new SequencePicker(status.Console, log);
        _pickableDisks = null;
    }

    // After a failed run the machine may pick again, and the disks are read again.
    public void ResetPicker()
    {
        _picker.Reset();
        _pickableDisks = null;
    }

    // Offers or withdraws the picker, and starts or stops typing to match. True while the picker is offered.
    public async Task<bool> UpdateAsync(Guid machineId, string token, bool signInWanted, bool pickWanted, CancellationToken cancellationToken)
    {
        if (!pickWanted)
        {
            ResetPicker();
        }
        else if (!_picker.IsOffered)
        {
            await OfferSequencesAsync(machineId, token, cancellationToken).ConfigureAwait(false);
        }

        if (_typing is not null && (_typingForPicker ? !_picker.IsOffered : !signInWanted))
        {
            await StopTypingAsync().ConfigureAwait(false);
        }

        if (_typing is null && (signInWanted || _picker.IsOffered))
        {
            _typingForPicker = !signInWanted;
            _stopTyping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _typing = _typingForPicker ? _picker.ReadAsync(_stopTyping.Token) : _conversation.ReadAsync(_stopTyping.Token);
        }

        return _picker.IsOffered;
    }

    // Returns the typed line once Typing has completed.
    public async Task<ConsoleAnswer?> TakeAnswerAsync()
    {
        ConsoleAnswer? answer = _typing is null ? null : await _typing.ConfigureAwait(false);
        _stopTyping?.Dispose();
        _stopTyping = null;
        _typing = null;

        return answer;
    }

    // Waits for the prompt to let go of the console before anything else can ask for input.
    public async Task StopTypingAsync()
    {
        if (_typing is null || _stopTyping is null)
        {
            return;
        }

        await _stopTyping.CancelAsync().ConfigureAwait(false);
        await _typing.ConfigureAwait(false);
        _stopTyping.Dispose();
        _stopTyping = null;
        _typing = null;
    }

    // A refused token and a stop are passed on to the caller.
    public Task SendAsync(Guid machineId, string token, ConsoleAnswer answer, CancellationToken cancellationToken) =>
        _typingForPicker
            ? SendPickAsync(machineId, token, answer, cancellationToken)
            : SendSignInAsync(machineId, token, answer, cancellationToken);

    private async Task SendSignInAsync(Guid machineId, string token, ConsoleAnswer answer, CancellationToken cancellationToken)
    {
        if (_conversation.Accept(answer) is not { } request)
        {
            return;
        }

        try
        {
            AgentSignInResult result = await server.SignInAsync(machineId, token, request, cancellationToken).ConfigureAwait(false);
            _conversation.Handle(result.Status);
        }
        catch (Exception exception) when (LoopCallRules.IsTransient(exception) && !cancellationToken.IsCancellationRequested)
        {
            _conversation.NotSent(exception);
        }
    }

    private async Task SendPickAsync(Guid machineId, string token, ConsoleAnswer answer, CancellationToken cancellationToken)
    {
        if (_picker.Accept(answer) is not { } request)
        {
            return;
        }

        ConfirmedDisk = _picker.ChosenDisk;

        try
        {
            AgentRun run = await server.PickSequenceAsync(machineId, token, request, cancellationToken).ConfigureAwait(false);
            PickedWithoutErase = request.DiskNumber is null ? run.Id : null;
            _picker.Picked(run);
        }
        catch (Exception exception) when (ServerCallRules.IsRefusal(exception))
        {
            _picker.Refused(ServerCallRules.Reason(exception, "the choice"), (exception as AgentRequestException)?.FieldErrors);
        }
        catch (Exception exception) when (LoopCallRules.IsTransient(exception) && !cancellationToken.IsCancellationRequested)
        {
            _picker.NotSent(exception);
        }
    }

    // Asks for the sequences until there are some. If a sequence erases a disk, the disks are read once per stretch in
    // which the machine may pick. Without a disk, only sequences that erase none are offered until a restart finds one.
    private async Task OfferSequencesAsync(Guid machineId, string token, CancellationToken cancellationToken)
    {
        IReadOnlyList<AgentSequenceChoice> sequences;

        try
        {
            sequences = await server.GetSequencesAsync(machineId, token, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (ServerCallRules.IsRefusal(exception))
        {
            // The machine may no longer pick. The next poll will say so.
            return;
        }

        if (sequences.Count == 0)
        {
            if (!_toldNoSequences)
            {
                _toldNoSequences = true;
                log.Warning("The server has no task sequence this machine can run. Create one on the Sequences page.");
            }

            return;
        }

        _toldNoSequences = false;

        if (_pickableDisks is null && sequences.Any(sequence => sequence.ErasesDisk))
        {
            _pickableDisks = await disks.ListDisksAsync(cancellationToken).ConfigureAwait(false);
            status.DisksRead(_pickableDisks);

            if (_pickableDisks.Count == 0)
            {
                log.Error(SequenceRunner.NoDiskMessage);
            }
        }

        MachineIdentity? identity = registrar.LastIdentity;
        _picker.Offer(sequences, _pickableDisks ?? [], identity?.SecureBootEnabled, identity?.TrustedUefiCas);
    }
}

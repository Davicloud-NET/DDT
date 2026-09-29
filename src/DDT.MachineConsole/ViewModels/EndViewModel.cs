// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Machine;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// What comes next once the agent has ended, or in DDT's session once the run is over. The person can close, or restart
// after a confirmation.
public sealed class EndViewModel : ObservableObject
{
    private readonly Localizer _l;
    private readonly IMachinePower _power;
    private readonly bool _session;
    private LinkEnd? _ended;
    private ConsoleStage? _stage;
    private bool _confirmingRestart;

    // close ends the console, or in DDT's session signs out.
    public EndViewModel(Localizer localizer, IMachinePower power, Action close, bool session)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(power);
        ArgumentNullException.ThrowIfNull(close);

        _l = localizer;
        _power = power;
        _session = session;
        RestartCommand = new Command(() => ConfirmingRestart = true, () => IsEnded && _power.CanRestart);
        ConfirmRestartCommand = new Command(RestartNow, () => ConfirmingRestart);
        CancelRestartCommand = new Command(() => ConfirmingRestart = false);
        CloseCommand = new Command(close, () => CanClose);
    }

    // True once the pipe has ended.
    public bool IsEnded => _ended is not null;

    // In DDT's session: the run is over, and the agent waits for someone to sign out.
    public bool IsRunOver => _session && _stage is ConsoleStage.Finished or ConsoleStage.Failed or ConsoleStage.Stopped;

    public bool ShowsEndBand => IsEnded || IsRunOver;

    public bool CanClose => IsEnded || IsRunOver;

    public string EndedTitle => _session
        ? _l.T("DDT is done with this machine")
        : _ended == LinkEnd.Broken ? _l.T("The connection to the agent broke") : _l.T("The agent has ended");

    public string EndedText => _session
        ? _l.T("Sign out to leave DDT's session. Windows then shows its sign-in screen.")
        : _l.T("This screen keeps what the agent showed last. Nothing more comes from it.");

    public bool CanRestart => _power.CanRestart;

    public bool ShowsRestartUnavailable => !_power.CanRestart && !_session;

    public string RestartLabel => _l.T("Restart the machine");

    public string RestartUnavailable => _l.T("The console restarts a machine only in Windows PE.");

    public string CloseLabel => _session ? _l.T("Sign out") : _l.T("Close the console");

    public string CloseHint => _l.T("The command prompt is behind the console.");

    public bool ConfirmingRestart
    {
        get => _confirmingRestart;
        private set
        {
            if (Set(ref _confirmingRestart, value))
            {
                ConfirmRestartCommand.Refresh();
            }
        }
    }

    public string ConfirmTitle => _l.T("Restart this machine now?");

    public string ConfirmText => _l.T("It starts again as its firmware says, from the network or from its disk.");

    public string ConfirmLabel => _l.T("Restart");

    public string CancelLabel => _l.T("Cancel");

    public Command RestartCommand { get; }

    public Command ConfirmRestartCommand { get; }

    public Command CancelRestartCommand { get; }

    public Command CloseCommand { get; }

    public void Update(ConsoleState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _stage = state.Stage;
        CloseCommand.Refresh();
        Raise(nameof(IsRunOver));
        Raise(nameof(ShowsEndBand));
        Raise(nameof(CanClose));
    }

    public void Ended(LinkEnd end)
    {
        _ended = end;
        RestartCommand.Refresh();
        CloseCommand.Refresh();
        Raise(nameof(IsEnded));
        Raise(nameof(ShowsEndBand));
        Raise(nameof(CanClose));
        Raise(nameof(EndedTitle));
    }

    public void Refresh() => RaiseAll();

    private void RestartNow()
    {
        ConfirmingRestart = false;

        if (IsEnded && _power.CanRestart)
        {
            _power.Restart();
        }
    }
}

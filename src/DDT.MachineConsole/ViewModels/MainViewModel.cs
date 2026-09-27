// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Input;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Machine;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

public enum Overlay
{
    None,
    Log,
    Machine,
    Licences,
}

// The whole console: what the agent sends, turned into the screen for its stage or the question it asks, with the log,
// the machine's details and the licences over it on their keys. Every state replaces the one before, log lines add up,
// and one question shows at a time until it is answered, withdrawn or replaced. When the pipe ends, the last state stays
// and the console offers to restart the machine or to close, which leaves the command prompt behind it. Shift+F10 opens
// a command prompt in front of the console at any time. Everything here runs on the UI thread.
//
// As the shell of DDT's session in the installed Windows the console is all there is on the screen, and runs as an
// account that only shows the run: nothing closes it and no command prompt opens. The agent may go and come back, as its
// service restarts with Windows, and the console waits for it meanwhile. Once the run is over, close signs out.
public sealed class MainViewModel : ObservableObject
{
    private readonly Localizer _l;
    private readonly IMachinePower _power;
    private readonly ICommandPrompt _prompt;
    private readonly Action<int, ConsoleAnswer> _send;
    private readonly Action _close;
    private readonly bool _session;
    private bool _detached;
    private ConsoleState? _state;
    private QuestionViewModel? _question;
    private ConsoleStage? _stageWhenAnswered;
    private StageViewModel? _stageScreen;
    private ScreenViewModel _screen;
    private Overlay _overlay;
    private LicencesViewModel? _licences;
    private LinkEnd? _ended;
    private bool _confirmingRestart;
    private ConsoleNotice _notice;
    private ConsoleNotice _shownNotice;
    private bool _isDark = true;
    private bool _languageChosen;

    // send takes an answer to the agent; close ends the console, or in DDT's session signs out. session is the console
    // as the shell of DDT's session in the installed Windows.
    public MainViewModel(
        Localizer localizer,
        IMachinePower power,
        ICommandPrompt prompt,
        Action<int, ConsoleAnswer> send,
        Action close,
        bool session = false)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(power);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(close);

        _l = localizer;
        _power = power;
        _prompt = prompt;
        _send = send;
        _close = close;
        _session = session;
        OpenPromptCommand = new Command(prompt.Open, () => !session);
        Log = new LogViewModel(localizer) { Closing = CloseOverlay };
        Machine = new MachineViewModel(localizer) { Closing = CloseOverlay, PromptCommand = session ? null : OpenPromptCommand };
        _stageScreen = new ConnectionViewModel(localizer);
        _screen = _stageScreen;
        localizer.Changed += (_, _) => Refresh();

        ToggleLogCommand = new Command(() => Toggle(Overlay.Log));
        ToggleMachineCommand = new Command(() => Toggle(Overlay.Machine));
        ToggleLicencesCommand = new Command(() => Toggle(Overlay.Licences));
        ToggleThemeCommand = new Command(() => IsDark = !IsDark);
        ToggleLanguageCommand = new Command(ToggleLanguage);
        CloseOverlayCommand = new Command(CloseOverlay);
        RestartCommand = new Command(() => ConfirmingRestart = true, () => IsEnded && _power.CanRestart);
        ConfirmRestartCommand = new Command(RestartNow, () => ConfirmingRestart);
        CancelRestartCommand = new Command(() => ConfirmingRestart = false);
        CloseCommand = new Command(close, () => CanClose);
    }

    public ConsoleState? State => _state;

    public ScreenViewModel Screen
    {
        get => _screen;
        private set => Set(ref _screen, value);
    }

    // The open question, or the one answered while the agent has not moved on yet.
    public QuestionViewModel? Question => _question;

    public LogViewModel Log { get; }

    public MachineViewModel Machine { get; }

    public LicencesViewModel Licences => _licences ??= new LicencesViewModel(_l) { Closing = CloseOverlay };

    public Overlay OverlayShown
    {
        get => _overlay;
        set
        {
            if (Set(ref _overlay, value))
            {
                Raise(nameof(OverlayContent));
                Raise(nameof(HasOverlay));
                Raise(nameof(Keys));
            }
        }
    }

    public ScreenViewModel? OverlayContent => OverlayShown switch
    {
        Overlay.Log => Log,
        Overlay.Machine => Machine,
        Overlay.Licences => Licences,
        _ => null,
    };

    public bool HasOverlay => OverlayShown != Overlay.None;

    public bool IsDark
    {
        get => _isDark;
        set
        {
            if (Set(ref _isDark, value))
            {
                Raise(nameof(Keys));
            }
        }
    }

    public Localizer Localizer => _l;

    // The header.
    public string MachineLabel => MachineFacts.Label(_l, _state?.Machine);

    public string Connection => _ended switch
    {
        LinkEnd.Closed => _l.T("The agent has ended"),
        LinkEnd.Broken => _l.T("The connection to the agent broke"),
        _ when _state is null || _detached => _l.T("Waiting for the agent"),
        _ when _state.Server.Problem is not null => _l.F("Cannot reach {server}", ("server", Say.Host(_state.Server.Address))),
        _ when _state.MachineId is null => _l.F("Connecting to {server}", ("server", Say.Host(_state.Server.Address))),
        _ => _l.F("Connected to {server}", ("server", Say.Host(_state.Server.Address))),
    };

    public bool IsDryRun => _state?.DryRun == true;

    public Tag DryRunTag => Tag.Of(_l.T("Dry run"), TagTone.Attention);

    // The footer: where the machine is on the network, and its Secure Boot.
    public IReadOnlyList<Fact> FooterFacts
    {
        get
        {
            List<Fact> facts = [];

            if (_state?.Machine is not { } machine)
            {
                return facts;
            }

            if (machine.IpAddresses.Count > 0)
            {
                facts.Add(new Fact(_l.T("IP address"), machine.IpAddresses[0], Mono: true));
            }

            if (machine.MacAddresses.Count > 0)
            {
                facts.Add(new Fact(_l.T("MAC address"), Say.Mac(machine.MacAddresses[0]), Mono: true));
            }

            if (machine.SecureBootEnabled is not null)
            {
                facts.Add(new Fact(_l.T("Secure Boot"), Say.SecureBoot(_l, machine.SecureBootEnabled)));
            }

            return facts;
        }
    }

    // The keys that work everywhere, named by what they do now.
    public IReadOnlyList<KeyHint> Keys =>
    [
        new("F1", _l.T("Log"), OverlayShown == Overlay.Log, ToggleLogCommand),
        new("F2", _l.T("Machine"), OverlayShown == Overlay.Machine, ToggleMachineCommand),
        new("F3", _l.T("Licences"), OverlayShown == Overlay.Licences, ToggleLicencesCommand),
        new("F4", IsDark ? _l.T("Light") : _l.T("Dark"), false, ToggleThemeCommand),
        new("F5", _l.Language == UiLanguage.German ? "English" : "Deutsch", false, ToggleLanguageCommand),
    ];

    public Command ToggleLogCommand { get; }

    public Command ToggleMachineCommand { get; }

    public Command ToggleLicencesCommand { get; }

    public Command ToggleThemeCommand { get; }

    public Command ToggleLanguageCommand { get; }

    public Command CloseOverlayCommand { get; }

    public Command RestartCommand { get; }

    public Command ConfirmRestartCommand { get; }

    public Command CancelRestartCommand { get; }

    public Command CloseCommand { get; }

    // Shift+F10, shown in the machine's details and once the agent has ended: the key strip has no room for it at
    // 1024 x 768 without cutting off the machine's address.
    public Command OpenPromptCommand { get; }

    public string PromptLabel => _l.T("Command prompt");

    // Once the pipe has ended.
    public bool IsEnded => _ended is not null;

    public bool IsSession => _session;

    // In DDT's session: the run is over, and the agent waits for someone to sign out.
    public bool IsRunOver => _session && _state?.Stage is ConsoleStage.Finished or ConsoleStage.Failed or ConsoleStage.Stopped;

    // The band with the way on, once the agent has ended or, in DDT's session, once the run is over.
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

    public bool CanOpenPrompt => !_session;

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

    // A short note above the keys: how to reach F1 to F12 when a laptop's top row sends media keys, or why the console
    // stays open when someone tries to close it.
    public ConsoleNotice Notice
    {
        get => _notice;
        private set
        {
            // The band keeps the last note's words while it fades out.
            if (value != ConsoleNotice.None)
            {
                _shownNotice = value;
            }

            if (Set(ref _notice, value))
            {
                Raise(nameof(HasNotice));
                Raise(nameof(NoticeText));
                Raise(nameof(NoticeKeys));
            }
        }
    }

    public bool HasNotice => Notice != ConsoleNotice.None;

    public string NoticeText => _shownNotice switch
    {
        ConsoleNotice.MediaKeys =>
            _l.T("This keyboard's top row sends media keys. Hold Fn with F1 to F12, or press Fn and Esc to lock them as function keys."),
        ConsoleNotice.CloseRefused =>
            _l.T("The console stays open while DDT works on this machine. Shift+F10 opens a command prompt."),
        ConsoleNotice.SessionCloseRefused => _l.T("The console stays open while DDT works on this machine."),
        ConsoleNotice.SignOutWithF9 => _l.T("The console stays open. F9 signs out of DDT's session."),
        _ => string.Empty,
    };

    public IReadOnlyList<string> NoticeKeys => _shownNotice switch
    {
        ConsoleNotice.MediaKeys => ["Fn"],
        ConsoleNotice.CloseRefused => ["Shift", "F10"],
        ConsoleNotice.SignOutWithF9 => ["F9"],
        _ => [],
    };

    // Someone closes the window, with Alt+F4 or its close button. While the agent works, a passer-by must not take the
    // console away, so it stays and says how to reach a prompt; once the agent has ended, F9 closes it anyway. In DDT's
    // session the console is the shell, and closing it would leave an empty screen, so it never closes that way.
    public bool RefuseClose()
    {
        if (_session)
        {
            Notice = IsRunOver ? ConsoleNotice.SignOutWithF9 : ConsoleNotice.SessionCloseRefused;

            return true;
        }

        if (IsEnded)
        {
            return false;
        }

        Notice = ConsoleNotice.CloseRefused;

        return true;
    }

    public string ConfirmTitle => _l.T("Restart this machine now?");

    public string ConfirmText => _l.T("It starts again as its firmware says, from the network or from its disk.");

    public string ConfirmLabel => _l.T("Restart");

    public string CancelLabel => _l.T("Cancel");

    // A message from the agent, in the order it sent them.
    public void Receive(ConsoleMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (IsEnded)
        {
            return;
        }

        switch (message)
        {
            case StateMessage state:
                ReceiveState(state.State);
                break;
            case LogMessage log:
                Log.Append(log.Lines);
                break;
            case QuestionMessage question:
                Ask(question.Id, question.Question);
                break;
            case WithdrawMessage withdraw:
                Withdraw(withdraw.Id);
                break;
        }
    }

    // The pipe has ended: nothing more comes, and no question can be answered any more.
    public void Ended(LinkEnd end)
    {
        if (IsEnded)
        {
            return;
        }

        _ended = end;
        _question = null;
        ShowScreen();
        RestartCommand.Refresh();
        CloseCommand.Refresh();
        Raise(nameof(IsEnded));
        Raise(nameof(ShowsEndBand));
        Raise(nameof(CanClose));
        Raise(nameof(EndedTitle));
        Raise(nameof(Connection));
        Raise(nameof(Question));
    }

    // In DDT's session the pipe ends when the agent's service stops, as it does when Windows restarts, and the console
    // waits for the agent to come back: the last state stays meanwhile.
    public void Detached()
    {
        _detached = true;
        _question = null;
        ShowScreen();
        Raise(nameof(Connection));
        Raise(nameof(Question));
    }

    // The agent is back, and sends the whole state and its newest lines again.
    public void Attached()
    {
        _detached = false;
        Log.Clear();
        Raise(nameof(Connection));
    }

    // A key pressed anywhere, without modifiers. True when the console used it.
    public bool Press(Key key) => Press(key, KeyModifiers.None);

    // A key pressed anywhere. Only Shift+F10 takes a modifier; every other key works alone.
    public bool Press(Key key, KeyModifiers modifiers)
    {
        // The note about closing has been read once the person presses on.
        if (Notice == ConsoleNotice.CloseRefused)
        {
            Notice = ConsoleNotice.None;
        }

        if (key == Key.F10 && modifiers == KeyModifiers.Shift)
        {
            if (!_session)
            {
                _prompt.Open();
            }

            return true;
        }

        if (modifiers != KeyModifiers.None)
        {
            return false;
        }

        if (IsMediaKey(key))
        {
            Notice = ConsoleNotice.MediaKeys;

            return true;
        }

        // A function key came through, so the person has found Fn.
        if (Notice == ConsoleNotice.MediaKeys && (key is >= Key.F1 and <= Key.F12 || key == Key.Escape))
        {
            Notice = ConsoleNotice.None;
        }

        if (ConfirmingRestart)
        {
            switch (key)
            {
                case Key.Enter:
                    RestartNow();
                    return true;
                case Key.Escape:
                    ConfirmingRestart = false;
                    return true;
            }
        }

        switch (key)
        {
            case Key.F1:
                Toggle(Overlay.Log);
                return true;
            case Key.F2:
                Toggle(Overlay.Machine);
                return true;
            case Key.F3:
                Toggle(Overlay.Licences);
                return true;
            case Key.F4:
                IsDark = !IsDark;
                return true;
            case Key.F5:
                ToggleLanguage();
                return true;
            case Key.F8 when IsEnded && _power.CanRestart:
                ConfirmingRestart = true;
                return true;
            case Key.F9 when CanClose:
                _close();
                return true;
            case Key.Escape when HasOverlay:
                OverlayShown = Overlay.None;
                return true;
            default:
                return false;
        }
    }

    // What a laptop's top row sends without Fn and Windows PE still turns into keys: sound and media, and the browser keys
    // some keyboards put there. Brightness and the like go to the firmware and never arrive.
    private static bool IsMediaKey(Key key) => key is Key.VolumeMute or Key.VolumeDown or Key.VolumeUp
        or Key.MediaPlayPause or Key.MediaNextTrack or Key.MediaPreviousTrack or Key.MediaStop
        or Key.BrowserBack or Key.BrowserForward or Key.BrowserRefresh or Key.BrowserSearch or Key.BrowserHome;

    private void ReceiveState(ConsoleState state)
    {
        _state = state;

        // An answered question stays until the agent moves on, which it does by another stage.
        if (_question is { IsSending: true } && _stageWhenAnswered != state.Stage)
        {
            _question = null;
            Raise(nameof(Question));
        }

        Machine.Update(state);
        ShowScreen();
        CloseCommand.Refresh();
        Raise(nameof(State));
        Raise(nameof(MachineLabel));
        Raise(nameof(Connection));
        Raise(nameof(FooterFacts));
        Raise(nameof(IsDryRun));
        Raise(nameof(IsRunOver));
        Raise(nameof(ShowsEndBand));
        Raise(nameof(CanClose));
        SpeakAsTheServerSays(state.Language);
    }

    // The language the server has the console speak, once the agent has registered, unless someone at the machine chose
    // one with F5 already: that choice stands.
    private void SpeakAsTheServerSays(string? language)
    {
        UiLanguage? wanted = language switch
        {
            "de" => UiLanguage.German,
            "en" => UiLanguage.English,
            _ => null,
        };

        if (!_languageChosen && wanted is { } chosen && chosen != _l.Language)
        {
            _l.Switch(chosen);
        }
    }

    private void Ask(int id, ConsoleQuestion question)
    {
        if (_question is null || !_question.Accept(id, question))
        {
            // A new question needs the person, so nothing stays over it.
            OverlayShown = Overlay.None;
            _question = question switch
            {
                SignInQuestion signIn => new SignInViewModel(_l, id, signIn, _state?.Machine.KeyboardLayout, Answer),
                SequenceQuestion sequence => new SequenceChoiceViewModel(_l, id, sequence, Answer),
                DiskQuestion disk => new DiskChoiceViewModel(_l, id, disk, Answer),
                ComputerNameQuestion computerName => new ComputerNameViewModel(_l, id, computerName, Answer),
                EraseQuestion erase => new EraseViewModel(_l, id, erase, Answer),
                SecureBootQuestion secureBoot => new SecureBootViewModel(_l, id, secureBoot, _state?.Machine.TrustedUefiCas, Answer),
                _ => null,
            };
        }

        ShowScreen();
        Raise(nameof(Question));
    }

    private void Withdraw(int id)
    {
        if (_question?.Id != id)
        {
            return;
        }

        _question = null;
        ShowScreen();
        Raise(nameof(Question));
    }

    private void Answer(int id, ConsoleAnswer answer)
    {
        _stageWhenAnswered = _state?.Stage;
        _send(id, answer);
    }

    // The question where there is one, otherwise the screen of the stage. A screen of the same kind is kept and updated,
    // so what it shows moves rather than being built again.
    private void ShowScreen()
    {
        if (_question is SignInViewModel signIn)
        {
            AuthorizationViewModel authorization = StageScreen<AuthorizationViewModel>(() => new AuthorizationViewModel(_l));
            authorization.SignIn = signIn;
            Screen = authorization;

            return;
        }

        if (_question is not null)
        {
            Screen = _question;

            return;
        }

        StageViewModel stage = (_state?.Stage ?? ConsoleStage.Starting) switch
        {
            ConsoleStage.Starting or ConsoleStage.Connecting => StageScreen<ConnectionViewModel>(() => new ConnectionViewModel(_l)),
            ConsoleStage.WaitingForAuthorization => StageScreen<AuthorizationViewModel>(() => new AuthorizationViewModel(_l)),
            ConsoleStage.WaitingForSequence or ConsoleStage.Choosing => StageScreen<WaitingViewModel>(() => new WaitingViewModel(_l)),
            _ => StageScreen<RunViewModel>(() => new RunViewModel(_l)),
        };

        if (stage is AuthorizationViewModel waiting)
        {
            waiting.SignIn = null;
        }

        Screen = stage;
    }

    private TScreen StageScreen<TScreen>(Func<TScreen> create)
        where TScreen : StageViewModel
    {
        if (_stageScreen is not TScreen screen)
        {
            screen = create();
            _stageScreen = screen;
        }

        if (_state is not null)
        {
            screen.Update(_state);
        }

        return screen;
    }

    private void CloseOverlay() => OverlayShown = Overlay.None;

    private void Toggle(Overlay overlay) => OverlayShown = OverlayShown == overlay ? Overlay.None : overlay;

    private void ToggleLanguage()
    {
        _languageChosen = true;
        _l.Switch(_l.Language == UiLanguage.German ? UiLanguage.English : UiLanguage.German);
    }

    private void RestartNow()
    {
        ConfirmingRestart = false;

        if (IsEnded && _power.CanRestart)
        {
            _power.Restart();
        }
    }

    private void Refresh()
    {
        _stageScreen?.Refresh();

        if (_question is not null)
        {
            _question.Refresh();
        }

        Log.Refresh();
        Machine.Refresh();
        _licences?.Refresh();
        RaiseAll();
    }
}

public enum ConsoleNotice
{
    None,
    MediaKeys,
    CloseRefused,
    SessionCloseRefused,
    SignOutWithF9,
}

// A key in the footer: what it is, what it does, and whether what it opens is open.
public sealed record KeyHint(string Key, string Label, bool IsActive, Command Command);

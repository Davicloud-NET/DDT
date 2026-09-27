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
public sealed class MainViewModel : ObservableObject
{
    private readonly Localizer _l;
    private readonly IMachinePower _power;
    private readonly ICommandPrompt _prompt;
    private readonly Action<int, ConsoleAnswer> _send;
    private readonly Action _close;
    private ConsoleState? _state;
    private QuestionViewModel? _question;
    private ConsoleStage? _stageWhenAnswered;
    private StageViewModel? _stageScreen;
    private ScreenViewModel _screen;
    private Overlay _overlay;
    private LicencesViewModel? _licences;
    private LinkEnd? _ended;
    private bool _confirmingRestart;
    private bool _isDark = true;

    // send takes an answer to the agent; close ends the console.
    public MainViewModel(Localizer localizer, IMachinePower power, ICommandPrompt prompt, Action<int, ConsoleAnswer> send, Action close)
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
        OpenPromptCommand = new Command(prompt.Open);
        Log = new LogViewModel(localizer) { Closing = CloseOverlay };
        Machine = new MachineViewModel(localizer) { Closing = CloseOverlay, PromptCommand = OpenPromptCommand };
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
        CloseCommand = new Command(close, () => IsEnded);
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
        _ when _state is null => _l.T("Waiting for the agent"),
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

    public string EndedTitle => _ended == LinkEnd.Broken ? _l.T("The connection to the agent broke") : _l.T("The agent has ended");

    public string EndedText => _l.T("This screen keeps what the agent showed last. Nothing more comes from it.");

    public bool CanRestart => _power.CanRestart;

    public string RestartLabel => _l.T("Restart the machine");

    public string RestartUnavailable => _l.T("The console restarts a machine only in Windows PE.");

    public string CloseLabel => _l.T("Close the console");

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
        Raise(nameof(EndedTitle));
        Raise(nameof(Connection));
        Raise(nameof(Question));
    }

    // A key pressed anywhere, without modifiers. True when the console used it.
    public bool Press(Key key) => Press(key, KeyModifiers.None);

    // A key pressed anywhere. Only Shift+F10 takes a modifier; every other key works alone.
    public bool Press(Key key, KeyModifiers modifiers)
    {
        if (key == Key.F10 && modifiers == KeyModifiers.Shift)
        {
            _prompt.Open();

            return true;
        }

        if (modifiers != KeyModifiers.None)
        {
            return false;
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
            case Key.F9 when IsEnded:
                _close();
                return true;
            case Key.Escape when HasOverlay:
                OverlayShown = Overlay.None;
                return true;
            default:
                return false;
        }
    }

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
        Raise(nameof(State));
        Raise(nameof(MachineLabel));
        Raise(nameof(Connection));
        Raise(nameof(FooterFacts));
        Raise(nameof(IsDryRun));
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

    private void ToggleLanguage() => _l.Switch(_l.Language == UiLanguage.German ? UiLanguage.English : UiLanguage.German);

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

// A key in the footer: what it is, what it does, and whether what it opens is open.
public sealed record KeyHint(string Key, string Label, bool IsActive, Command Command);

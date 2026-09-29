// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Input;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Machine;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The whole console, on the UI thread. It turns the agent's messages into the screen for its stage or question. The
// log, the machine's details and the licences open over it on their keys.
public sealed class MainViewModel : ObservableObject
{
    private readonly Localizer _l;
    private readonly Action<int, ConsoleAnswer> _send;
    private readonly bool _session;
    private readonly ConsoleKeys _keys;
    private readonly StageScreens _stages;
    private ConsoleState? _state;
    private QuestionViewModel? _question;
    private ConsoleStage? _stageWhenAnswered;
    private ConsoleActivity? _activityWhenAnswered;
    private ScreenViewModel _screen;
    private Overlay _overlay;
    private LicencesViewModel? _licences;
    private bool _isDark = true;
    private bool _languageChosen;

    // send passes an answer to the agent. session means the console is the shell of DDT's session in the installed
    // Windows. Then there's no command prompt, and only F9 closes it, once the run is over.
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
        _send = send;
        _session = session;
        OpenPromptCommand = new Command(prompt.Open, () => !session);
        Log = new LogViewModel(localizer) { Closing = CloseOverlay };
        Machine = new MachineViewModel(localizer) { Closing = CloseOverlay, PromptCommand = session ? null : OpenPromptCommand };
        Header = new HeaderViewModel(localizer);
        Notice = new NoticeViewModel(localizer);
        End = new EndViewModel(localizer, power, close, session);
        _stages = new StageScreens(localizer);
        _screen = _stages.Current;
        localizer.Changed += (_, _) => Refresh();

        ToggleLogCommand = new Command(() => Toggle(Overlay.Log));
        ToggleMachineCommand = new Command(() => Toggle(Overlay.Machine));
        ToggleLicencesCommand = new Command(() => Toggle(Overlay.Licences));
        ToggleThemeCommand = new Command(() => IsDark = !IsDark);
        ToggleLanguageCommand = new Command(ToggleLanguage);
        CloseOverlayCommand = new Command(CloseOverlay, () => HasOverlay);
        _keys = new ConsoleKeys(Notice, End, OpenPromptCommand, new Dictionary<Key, Command>
        {
            [Key.F1] = ToggleLogCommand,
            [Key.F2] = ToggleMachineCommand,
            [Key.F3] = ToggleLicencesCommand,
            [Key.F4] = ToggleThemeCommand,
            [Key.F5] = ToggleLanguageCommand,
            [Key.F8] = End.RestartCommand,
            [Key.F9] = End.CloseCommand,
            [Key.Escape] = CloseOverlayCommand,
        });
    }

    public ConsoleState? State => _state;

    public ScreenViewModel Screen
    {
        get => _screen;
        private set => Set(ref _screen, value);
    }

    // The open question, or the answered one while the agent hasn't moved on yet.
    public QuestionViewModel? Question => _question;

    public HeaderViewModel Header { get; }

    public NoticeViewModel Notice { get; }

    public EndViewModel End { get; }

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
                CloseOverlayCommand.Refresh();
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

    public IReadOnlyList<Fact> FooterFacts => MachineFacts.Footer(_l, _state?.Machine);

    // The keys that work on every screen, labelled with what they do right now.
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

    // Shift+F10. It shows in the machine's details and once the agent has ended. The key strip has no room for it at
    // 1024 x 768 without cutting off the machine's address.
    public Command OpenPromptCommand { get; }

    public string PromptLabel => _l.T("Command prompt");

    public bool CanOpenPrompt => !_session;

    public bool IsSession => _session;

    // Handles a message from the agent. Messages arrive in the order the agent sent them.
    public void Receive(ConsoleMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (End.IsEnded)
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

    // The pipe has ended. Nothing more arrives, and no question can be answered any more.
    public void Ended(LinkEnd end)
    {
        if (End.IsEnded)
        {
            return;
        }

        _question = null;
        ShowScreen();
        End.Ended(end);
        Header.Ended(end);
        Raise(nameof(Question));
    }

    // In DDT's session the pipe ends whenever the agent's service stops. The last state stays until the agent is back.
    public void Detached()
    {
        _question = null;
        ShowScreen();
        Header.IsDetached = true;
        Raise(nameof(Question));
    }

    // The agent is back, and sends the whole state and its newest lines again.
    public void Attached()
    {
        Log.Clear();
        Header.IsDetached = false;
    }

    public bool Press(Key key) => Press(key, KeyModifiers.None);

    // Returns true if the console handled the key.
    public bool Press(Key key, KeyModifiers modifiers) => _keys.Press(key, modifiers);

    // For Alt+F4 and the window's close button. A passer-by must not close the console while the agent works. In DDT's
    // session the console is the shell, so closing it that way would leave an empty screen.
    public bool RefuseClose()
    {
        if (_session)
        {
            Notice.Show(End.IsRunOver ? ConsoleNotice.SignOutWithF9 : ConsoleNotice.SessionCloseRefused);

            return true;
        }

        if (End.IsEnded)
        {
            return false;
        }

        Notice.Show(ConsoleNotice.CloseRefused);

        return true;
    }

    private void ReceiveState(ConsoleState state)
    {
        _state = state;

        // An answered question stays until the agent moves on. That means another stage, or for a run's questions like
        // a Pause step, another activity of the run.
        if (_question is { IsSending: true } && (_stageWhenAnswered != state.Stage || _activityWhenAnswered != state.Run?.Activity))
        {
            _question = null;
            Raise(nameof(Question));
        }

        _question?.Update(state);

        Machine.Update(state);
        ShowScreen();
        Raise(nameof(State));
        Header.Update(state);
        Raise(nameof(FooterFacts));
        End.Update(state);
        SpeakAsTheServerSays(state.Language);
        Header.ShowLogo(state.Logo);
    }

    // A language picked with F5 at the machine overrides the server's.
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
            // A new question needs the person's attention, so any open overlay closes.
            OverlayShown = Overlay.None;
            _question = QuestionScreens.Create(_l, id, question, _state, Answer);
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
        _activityWhenAnswered = _state?.Run?.Activity;
        _send(id, answer);
    }

    private void ShowScreen()
    {
        if (_question is SignInViewModel signIn)
        {
            AuthorizationViewModel authorization = _stages.Authorization(_state);
            authorization.SignIn = signIn;
            Screen = authorization;

            return;
        }

        if (_question is not null)
        {
            Screen = _question;

            return;
        }

        StageViewModel stage = _stages.For(_state);

        if (stage is AuthorizationViewModel waiting)
        {
            waiting.SignIn = null;
        }

        Screen = stage;
    }

    private void CloseOverlay() => OverlayShown = Overlay.None;

    private void Toggle(Overlay overlay) => OverlayShown = OverlayShown == overlay ? Overlay.None : overlay;

    private void ToggleLanguage()
    {
        _languageChosen = true;
        _l.Switch(_l.Language == UiLanguage.German ? UiLanguage.English : UiLanguage.German);
    }

    private void Refresh()
    {
        _stages.Current.Refresh();
        _question?.Refresh();
        Log.Refresh();
        Machine.Refresh();
        _licences?.Refresh();
        Header.Refresh();
        Notice.Refresh();
        End.Refresh();
        RaiseAll();
    }
}

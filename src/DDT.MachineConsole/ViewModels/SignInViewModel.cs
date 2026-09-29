// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// Sign-in with a DDT account, one field at a time as the agent asks for it. The password is cleared from the screen
// once it's sent. Nothing typed here is ever logged.
public sealed class SignInViewModel : QuestionViewModel
{
    private SignInQuestion _question;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string _code = string.Empty;

    public SignInViewModel(Localizer localizer, int id, SignInQuestion question, string? keyboardLayout, Action<int, ConsoleAnswer> answer)
        : base(localizer, id, answer)
    {
        ArgumentNullException.ThrowIfNull(question);

        _question = question;
        KeyboardLayout = keyboardLayout;
        _userName = question.UserName ?? string.Empty;
    }

    public SignInField Field => _question.Field;

    public bool AsksUserName => Field == SignInField.UserName;

    public bool AsksPassword => Field == SignInField.Password;

    public bool AsksCode => Field == SignInField.Code;

    // If the user name is already known, it shows as text above the field being asked.
    public bool ShowsUserName => !AsksUserName && !string.IsNullOrEmpty(_question.UserName);

    public string? KeyboardLayout { get; private set; }

    public string Title => T("Sign in at this machine");

    public string Intro => T("Sign in with your DDT account. An operator or administrator authorizes this machine by signing in.");

    public string UserNameLabel => T("User name");

    public string PasswordLabel => T("Password");

    public string CodeLabel => T("Authenticator code");

    public string SignedInAs => F("Signing in as {name}", ("name", _question.UserName ?? string.Empty));

    public string UserName
    {
        get => _userName;
        set
        {
            if (Set(ref _userName, value))
            {
                RefreshCommands();
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (Set(ref _password, value))
            {
                RefreshCommands();
            }
        }
    }

    public string Code
    {
        get => _code;
        set
        {
            if (Set(ref _code, value))
            {
                RefreshCommands();
            }
        }
    }

    // The agent's error message about the previous attempt, like a wrong password.
    public string? Error => _question.Error;

    public bool HasError => !string.IsNullOrEmpty(_question.Error);

    public bool HasKeyboardLayout => !string.IsNullOrEmpty(KeyboardLayout);

    public string KeyboardHint => F(
        "The keyboard layout is {layout}. A password typed with another layout is refused as a wrong one.",
        ("layout", KeyboardLayout ?? string.Empty));

    public string SubmitLabel => AsksUserName ? T("Next") : T("Sign in");

    public string BackLabel => AsksCode ? T("Back to the password") : T("Use another account");

    public override bool CanSubmit => Field switch
    {
        SignInField.UserName => !string.IsNullOrWhiteSpace(UserName),
        SignInField.Password => Password.Length > 0,
        _ => !string.IsNullOrWhiteSpace(Code),
    };

    public override bool CanGoBack => !AsksUserName;

    public override bool Accept(int id, ConsoleQuestion question)
    {
        if (question is not SignInQuestion signIn)
        {
            return false;
        }

        _question = signIn;

        if (signIn.UserName is { } userName)
        {
            _userName = userName;
        }

        _password = string.Empty;
        _code = string.Empty;
        Reopen(id);
        RaiseAll();
        RefreshCommands();

        return true;
    }

    public void KeyboardLayoutIs(string? layout)
    {
        if (layout != KeyboardLayout)
        {
            KeyboardLayout = layout;
            Raise(nameof(KeyboardLayout));
            Raise(nameof(HasKeyboardLayout));
            Raise(nameof(KeyboardHint));
        }
    }

    protected override ConsoleAnswer? Answer() => Field switch
    {
        SignInField.UserName => new ConsoleAnswer(Text: UserName.Trim()),
        SignInField.Password => new ConsoleAnswer(Text: Password),
        _ => new ConsoleAnswer(Text: Code.Trim()),
    };

    // An empty password goes back to the user name, and an empty code back to the password.
    protected override ConsoleAnswer? BackAnswer() => new(Text: string.Empty);

    protected override void Sent()
    {
        Password = string.Empty;
        Code = string.Empty;
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// An account for the run: a user name and a password. The password is masked, sent once and then forgotten.
public sealed class AccountFieldViewModel : InputFieldViewModel
{
    private string _userName;
    private string _password = string.Empty;

    public AccountFieldViewModel(Localizer localizer, ConsoleInput input, string? keyboardLayout, Action changed)
        : base(localizer, input, changed)
    {
        _userName = input.Default ?? string.Empty;
        KeyboardLayout = keyboardLayout;
    }

    public string UserName
    {
        get => _userName;
        set
        {
            if (Set(ref _userName, value ?? string.Empty))
            {
                Changed();
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (Set(ref _password, value ?? string.Empty))
            {
                Changed();
            }
        }
    }

    public string UserNameLabel => L.T("User name");

    public string PasswordLabel => L.T("Password");

    // Says what the account is for, that DDT keeps its password for the run, and which keyboard layout it's typed with.
    public string Note => HasKeyboardLayout ? $"{Keeping} {KeyboardHint}" : Keeping;

    public string? KeyboardLayout { get; private set; }

    public bool HasKeyboardLayout => !string.IsNullOrEmpty(KeyboardLayout);

    public string KeyboardHint => L.F(
        "The keyboard layout is {layout}. A password typed with another layout would be wrong.",
        ("layout", KeyboardLayout ?? string.Empty));

    // Both are filled in. If the account is optional, neither may be.
    public override bool IsAnswered => (HasUserName && Password.Length > 0) || (IsOptional && !HasUserName && Password.Length == 0);

    private bool HasUserName => !string.IsNullOrWhiteSpace(UserName);

    private string Keeping => string.IsNullOrWhiteSpace(Input.Domain)
        ? L.T("DDT keeps it for this run only and never shows it.")
        : L.F("For {domain}. DDT keeps it for this run only and never shows it.", ("domain", Input.Domain));

    public override ConsoleInputValue Value() =>
        HasUserName ? new ConsoleInputValue(Input.Name, null, UserName.Trim(), Password) : new ConsoleInputValue(Input.Name, null);

    public override void ForgetSecrets() => Password = string.Empty;

    public void KeyboardLayoutIs(string? layout)
    {
        KeyboardLayout = layout;
        Raise(nameof(KeyboardLayout));
        Raise(nameof(HasKeyboardLayout));
        Raise(nameof(KeyboardHint));
        Raise(nameof(Note));
    }
}

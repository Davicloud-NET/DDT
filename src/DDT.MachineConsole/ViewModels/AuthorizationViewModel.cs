// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// Registered, not yet authorized: the two ways on, signing in here or an approval on the Machines page, with what
// tells the machine apart on that page, and the sign-in while the agent asks for it.
public sealed class AuthorizationViewModel(Localizer localizer) : StageViewModel(localizer)
{
    private ConsoleState? _state;
    private SignInViewModel? _signIn;

    public SignInViewModel? SignIn
    {
        get => _signIn;
        set
        {
            if (Set(ref _signIn, value))
            {
                Raise(nameof(HasSignIn));
                Raise(nameof(Intro));
            }
        }
    }

    public bool HasSignIn => SignIn is not null;

    public string Title => T("Authorize this machine");

    public Tag Tag => Tag.Of(T("Waiting"), TagTone.Attention);

    public string Intro => _state?.SignedInBy is { } name
        ? F("{name} signed in here. An operator or administrator still has to approve this machine on the Machines page.", ("name", name))
        : HasSignIn
            ? T("Sign in here with your DDT account, or approve this machine on the Machines page. Either way the agent goes on by itself.")
            : T("Approve this machine on the Machines page. The agent goes on by itself as soon as it is approved.");

    public string FindLabel => T("Find it on the Machines page by");

    public IReadOnlyList<Fact> Identity => _state is null ? [] : MachineFacts.Identity(L, _state.Machine);

    public override void Update(ConsoleState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _state = state;
        SignIn?.KeyboardLayoutIs(state.Machine.KeyboardLayout);
        Raise(nameof(Intro));
        Raise(nameof(Identity));
    }

    public override void Refresh()
    {
        base.Refresh();
        SignIn?.Refresh();
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The screen for each stage of the agent. A screen of the same kind is kept and updated, so its content changes in
// place instead of being built again.
public sealed class StageScreens
{
    private readonly Localizer _l;
    private StageViewModel _current;

    public StageScreens(Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        _l = localizer;
        _current = new ConnectionViewModel(localizer);
    }

    // The last stage screen kept, or the starting screen before the agent has said anything. A question may cover it.
    public StageViewModel Current => _current;

    public StageViewModel For(ConsoleState? state) => (state?.Stage ?? ConsoleStage.Starting) switch
    {
        ConsoleStage.Starting or ConsoleStage.Connecting => Kept(state, () => new ConnectionViewModel(_l)),
        ConsoleStage.WaitingForAuthorization => Authorization(state),
        ConsoleStage.WaitingForSequence or ConsoleStage.Choosing => Kept(state, () => new WaitingViewModel(_l)),
        _ => Kept(state, () => new RunViewModel(_l)),
    };

    // The sign-in also shows on this screen, whatever the stage.
    public AuthorizationViewModel Authorization(ConsoleState? state) => Kept(state, () => new AuthorizationViewModel(_l));

    private TScreen Kept<TScreen>(ConsoleState? state, Func<TScreen> create)
        where TScreen : StageViewModel
    {
        if (_current is not TScreen screen)
        {
            screen = create();
            _current = screen;
        }

        if (state is not null)
        {
            screen.Update(state);
        }

        return screen;
    }
}

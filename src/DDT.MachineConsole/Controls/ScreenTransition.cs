// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Input;

namespace DDT.MachineConsole.Controls;

// The old screen leaves before the new one enters. Two screens of text faded over each other are unreadable.
public sealed class ScreenTransition : IPageTransition
{
    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (to is not null)
        {
            // The new screen stays hidden while the old one leaves.
            to.Opacity = 0;

            if (to is InputElement entering)
            {
                entering.IsHitTestVisible = true;
            }
        }

        if (from is not null)
        {
            // The leaving screen doesn't take clicks any more.
            if (from is InputElement leaving)
            {
                leaving.IsHitTestVisible = false;
            }

            await Motion.LeaveAsync(from, cancellationToken).ConfigureAwait(true);
        }

        if (to is not null && !cancellationToken.IsCancellationRequested)
        {
            await Motion.EnterAsync(to, 0, cancellationToken).ConfigureAwait(true);
        }
    }
}

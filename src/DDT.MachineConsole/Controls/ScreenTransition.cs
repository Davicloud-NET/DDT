// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Input;

namespace DDT.MachineConsole.Controls;

// One screen giving way to the next, for a new stage or question, or another overlay: the old one fades out over fast,
// and then the new one fades in and rises into place over normal. One after the other, since two screens of text
// faded over each other are read as neither.
public sealed class ScreenTransition : IPageTransition
{
    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (to is not null)
        {
            // Nothing of the new screen shows while the old one leaves.
            to.Opacity = 0;

            if (to is InputElement entering)
            {
                entering.IsHitTestVisible = true;
            }
        }

        if (from is not null)
        {
            // What leaves takes no click any more.
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

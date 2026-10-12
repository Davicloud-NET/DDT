// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;

namespace DDT.Agent;

// Waits for a key at the text console. Keys are polled, as for a typed line, because a read cannot be cancelled.
public sealed class ConsoleKeyWait(TimeProvider timeProvider, IConsoleKeys? console = null) : IKeyWait
{
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(50);

    private readonly IConsoleKeys _console = console ?? new SystemConsoleKeys();

    public bool CanPress => !_console.IsRedirected;

    public async Task<bool> PressedWithinAsync(TimeSpan time, CancellationToken cancellationToken)
    {
        if (!CanPress)
        {
            await Task.Delay(time, timeProvider, cancellationToken).ConfigureAwait(false);

            return false;
        }

        // A key pressed while the machine started must not decide anything here.
        Discard();

        for (TimeSpan waited = TimeSpan.Zero; waited < time; waited += s_pollInterval)
        {
            if (_console.KeyAvailable)
            {
                Discard();

                return true;
            }

            await Task.Delay(s_pollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    // The key itself means nothing, and must not land in a prompt that comes later.
    private void Discard()
    {
        while (_console.KeyAvailable)
        {
            _console.TakeKey();
        }
    }
}

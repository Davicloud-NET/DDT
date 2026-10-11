// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;

namespace DDT.Agent.Tests;

// A keyboard that answers each wait at once: with a key when Pressed is set, else as if the time had passed. Waits
// lists how long each wait was to last. With Never a wait only ends when it is cancelled.
internal sealed class FakeKeyWait : IKeyWait
{
    public bool CanPress { get; init; } = true;

    public bool Pressed { get; set; }

    public bool Never { get; init; }

    public List<TimeSpan> Waits { get; } = [];

    // Runs when a wait begins, for a test that wants to see what was done by then.
    public Action? OnWait { get; set; }

    public async Task<bool> PressedWithinAsync(TimeSpan time, CancellationToken cancellationToken)
    {
        Waits.Add(time);
        OnWait?.Invoke();

        if (Never)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return Pressed;
    }
}

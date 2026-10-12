// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;

namespace DDT.Agent.Tests;

// A keyboard whose keys a test types: some before anyone waits, and some once it was asked a number of times.
internal sealed class FakeConsoleKeys : IConsoleKeys
{
    public bool IsRedirected { get; init; }

    // The keys that wait to be taken.
    public int Waiting { get; set; }

    // At which question for a key more keys arrive, and how many. 0 for never.
    public int ArriveAtPoll { get; init; }

    public int Arriving { get; init; } = 1;

    public int Polls { get; private set; }

    public int Taken { get; private set; }

    public bool KeyAvailable
    {
        get
        {
            if (++Polls == ArriveAtPoll)
            {
                Waiting += Arriving;
            }

            return Waiting > 0;
        }
    }

    public void TakeKey()
    {
        Waiting--;
        Taken++;
    }
}

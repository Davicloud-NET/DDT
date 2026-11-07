// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Consoles;

// The keyboard of the text console, as a wait for a key polls it.
public interface IConsoleKeys
{
    // True where nobody types, as when the agent's input comes from a file or a pipe.
    bool IsRedirected { get; }

    bool KeyAvailable { get; }

    // Takes the next key off the console without showing it.
    void TakeKey();
}

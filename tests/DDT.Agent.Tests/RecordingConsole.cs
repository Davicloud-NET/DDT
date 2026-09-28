// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;

namespace DDT.Agent.Tests;

// Keeps every state the console is shown, and asks nothing.
internal sealed class RecordingConsole(List<ConsoleState> shown) : IMachineConsole
{
    public bool CanAsk => false;

    public void Show(ConsoleState state)
    {
        lock (shown)
        {
            shown.Add(state);
        }
    }

    public void Write(ConsoleLogLine line)
    {
    }

    public Task<ConsoleAnswer?> AskAsync(ConsoleQuestion question, CancellationToken cancellationToken) => Task.FromResult<ConsoleAnswer?>(null);
}

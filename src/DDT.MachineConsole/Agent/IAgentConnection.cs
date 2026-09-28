// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.Agent;

// The console's end of the pipe once the agent has accepted it: what the agent sends, and the answers going back.
public interface IAgentConnection : IAsyncDisposable
{
    // The next message, or null once the agent closed the pipe.
    Task<ConsoleMessage?> ReceiveAsync(CancellationToken cancellationToken);

    Task AnswerAsync(int questionId, ConsoleAnswer answer, CancellationToken cancellationToken);
}

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

// ConsoleClient as the console uses it.
public sealed class ClientConnection(ConsoleClient client) : IAgentConnection
{
    // How long the agent's pipe may take to appear and say hello, as the agent waits for the console.
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    // Throws ConsoleProtocolException when the agent refuses this console, and TimeoutException or IOException when
    // there is no agent to talk to.
    public static async Task<ClientConnection> ConnectAsync(string pipeName, string program, CancellationToken cancellationToken) =>
        new(await ConsoleClient.ConnectAsync(pipeName, program, ConnectTimeout, cancellationToken).ConfigureAwait(false));

    public Task<ConsoleMessage?> ReceiveAsync(CancellationToken cancellationToken) => client.ReceiveAsync(cancellationToken);

    public Task AnswerAsync(int questionId, ConsoleAnswer answer, CancellationToken cancellationToken) =>
        client.AnswerAsync(questionId, answer, cancellationToken);

    public ValueTask DisposeAsync() => client.DisposeAsync();
}

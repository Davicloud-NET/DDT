// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using System.Security.Principal;
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

    // In DDT's session in the installed Windows the agent runs as SYSTEM and the console as the session's account, so
    // the pipe is not the console's own: it talks only to a pipe SYSTEM owns, which no program of the session can make.
    // Throws UnauthorizedAccessException for any other.
    public static async Task<ClientConnection> ConnectToSessionAgentAsync(
        string pipeName,
        string program,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        HelloMessage hello = new(HelloMessage.CurrentVersion, program);

        return new(await ConsoleClient.ConnectAsync(pipe, hello, OwnedBySystem, timeout, cancellationToken).ConfigureAwait(false));
    }

    public static void OwnedBySystem(NamedPipeClientStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        if (pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner
            || !owner.IsWellKnown(WellKnownSidType.LocalSystemSid))
        {
            throw new UnauthorizedAccessException("The pipe is not the agent's: SYSTEM does not own it.");
        }
    }

    public Task<ConsoleMessage?> ReceiveAsync(CancellationToken cancellationToken) => client.ReceiveAsync(cancellationToken);

    public Task AnswerAsync(int questionId, ConsoleAnswer answer, CancellationToken cancellationToken) =>
        client.AnswerAsync(questionId, answer, cancellationToken);

    public ValueTask DisposeAsync() => client.DisposeAsync();
}

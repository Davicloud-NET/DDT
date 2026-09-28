// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using System.Security.Principal;
using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.Agent;

// IAgentConnection over the protocol's ConsoleClient.
public sealed class ClientConnection(ConsoleClient client) : IAgentConnection
{
    // How long the agent's pipe may take to appear and say hello, as the agent waits for the console.
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    // Throws ConsoleProtocolException when the agent refuses this console, and TimeoutException or IOException when
    // there is no agent to talk to.
    public static async Task<ClientConnection> ConnectAsync(string pipeName, string program, CancellationToken cancellationToken) =>
        new(await ConsoleClient.ConnectAsync(pipeName, program, ConnectTimeout, cancellationToken).ConfigureAwait(false));

    // In DDT's session the agent runs as SYSTEM, and a pipe SYSTEM owns is one no program of the session can have made.
    // Throws UnauthorizedAccessException for any other pipe.
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

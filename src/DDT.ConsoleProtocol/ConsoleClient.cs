// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;

namespace DDT.ConsoleProtocol;

// The console's end of the pipe. ConnectAsync connects, sends a hello and waits for the agent's hello. Then the console
// reads what the agent sends and answers questions, until ReceiveAsync returns null when the agent ends.
public sealed class ConsoleClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly ConsoleChannel _channel;

    private ConsoleClient(NamedPipeClientStream pipe, ConsoleChannel channel, HelloMessage agent)
    {
        _pipe = pipe;
        _channel = channel;
        Agent = agent;
    }

    // The agent's hello.
    public HelloMessage Agent { get; }

    // program names this console for the agent's log. Throws ConsoleProtocolException when the agent refuses it.
    public static Task<ConsoleClient> ConnectAsync(
        string pipeName,
        string program,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        ConnectAsync(pipeName, new HelloMessage(HelloMessage.CurrentVersion, program), timeout, cancellationToken);

    // Connects with a hello of any version. Only tests need this.
    public static Task<ConsoleClient> ConnectAsync(
        string pipeName,
        HelloMessage hello,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        ConnectAsync(ConsolePipe.CreateClient(pipeName), hello, null, timeout, cancellationToken);

    // Connects over a pipe the caller made, such as one to an agent running as another account. check runs before
    // anything is sent, and throws UnauthorizedAccessException when the pipe isn't the agent's. From here on the client
    // owns the pipe.
    public static async Task<ConsoleClient> ConnectAsync(
        NamedPipeClientStream pipe,
        HelloMessage hello,
        Action<NamedPipeClientStream>? check,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(hello);

        ConsoleChannel channel = new(pipe);

        try
        {
            await pipe.ConnectAsync(timeout, cancellationToken).ConfigureAwait(false);
            check?.Invoke(pipe);
            await channel.SendAsync(hello, cancellationToken).ConfigureAwait(false);

            return await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false) switch
            {
                HelloMessage agent => new ConsoleClient(pipe, channel, agent),
                RefusedMessage refused => throw new ConsoleProtocolException(refused.Reason),
                null => throw new ConsoleProtocolException("The agent closed the pipe without a hello."),
                _ => throw new ConsoleProtocolException("The agent did not answer with a hello."),
            };
        }
        catch
        {
            channel.Dispose();
            await pipe.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    public Task<ConsoleMessage?> ReceiveAsync(CancellationToken cancellationToken) => _channel.ReceiveAsync(cancellationToken);

    public Task AnswerAsync(int questionId, ConsoleAnswer answer, CancellationToken cancellationToken) =>
        _channel.SendAsync(new AnswerMessage(questionId, answer), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _channel.Dispose();
        await _pipe.DisposeAsync().ConfigureAwait(false);
    }
}

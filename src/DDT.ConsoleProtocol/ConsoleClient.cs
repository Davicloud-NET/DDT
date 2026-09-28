// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;

namespace DDT.ConsoleProtocol;

// The console's end of the pipe. ConnectAsync connects, says hello and waits for the agent's; then the console reads
// what the agent sends until ReceiveAsync returns null, which it does when the agent ends, and answers questions.
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

    // With a hello of any version, which only a test needs.
    public static Task<ConsoleClient> ConnectAsync(
        string pipeName,
        HelloMessage hello,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        ConnectAsync(ConsolePipe.CreateClient(pipeName), hello, null, timeout, cancellationToken);

    // Over a pipe the caller made, such as one to an agent of another account. check runs before anything is sent and
    // throws UnauthorizedAccessException when the pipe is not the agent's. The client owns the pipe from here on.
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

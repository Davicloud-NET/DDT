// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.Agent;

// Why the pipe to the agent ended.
public enum LinkEnd
{
    // The agent closed it, as it does when it ends.
    Closed,

    // It broke, or the agent sent what is not a message of the protocol.
    Broken,
}

// Reads everything the agent sends, all the time, and hands each message on in order; answers go back from any thread.
// Reading never waits for the screen, so the agent never finds its console slow.
public sealed class AgentLink(IAgentConnection connection) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();

    // Completes when the pipe has ended, with why. received gets each message on the reading thread.
    public async Task<LinkEnd> ReadAsync(Action<ConsoleMessage> received)
    {
        ArgumentNullException.ThrowIfNull(received);

        try
        {
            while (await connection.ReceiveAsync(_stop.Token).ConfigureAwait(false) is { } message)
            {
                received(message);
            }

            return LinkEnd.Closed;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            return LinkEnd.Closed;
        }
        catch (Exception)
        {
            // Whatever it was, nothing more comes, and the screen has to say so: a broken pipe, what is not a
            // message of the protocol, or the pipe closed under the reader.
            return LinkEnd.Broken;
        }
    }

    // False when the answer could not be sent, because the pipe has ended.
    public async Task<bool> AnswerAsync(int questionId, ConsoleAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        try
        {
            await connection.AnswerAsync(questionId, answer, _stop.Token).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
    }
}

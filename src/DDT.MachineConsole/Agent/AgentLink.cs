// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.Agent;

// Reads the agent's messages without waiting for the UI thread, so a busy UI never makes the agent wait.
// AnswerAsync can be called from any thread.
public sealed class AgentLink(IAgentConnection connection) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();

    // Returns why the pipe ended. The received callback runs on the reading thread.
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
            // The reading thread stops here. An IOException, a malformed message or a pipe disposed under the reader
            // all end the link, and the screen shows it.
            return LinkEnd.Broken;
        }
    }

    // Returns false if the pipe has ended and the answer couldn't be sent.
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

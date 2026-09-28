// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.Agent;

// Reads the agent's messages without ever waiting for the UI thread, so the agent never finds its console slow.
// AnswerAsync may be called from any thread.
public sealed class AgentLink(IAgentConnection connection) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();

    // Completes with why the pipe ended. received runs on the reading thread.
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
            // The reading thread's boundary: an IOException, a malformed message or a pipe disposed under the reader
            // all end the link, and the screen says so.
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

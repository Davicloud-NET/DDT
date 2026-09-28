// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// Sends to a console over a pipe, and gives up on one that stops reading.
internal static class ConsoleSender
{
    // A console that takes no message for this long has stopped reading.
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    // Sends what the outbox holds as it comes. Null once the outbox closes and everything is sent, otherwise why not.
    public static async Task<string?> SendQueuedAsync(ConsoleChannel channel, ConsoleOutbox outbox, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (outbox.Take(out Task wake) is not { } messages)
            {
                return null;
            }

            if (messages.Count == 0)
            {
                await wake.WaitAsync(cancellationToken).ConfigureAwait(false);

                continue;
            }

            foreach (ConsoleMessage message in messages)
            {
                if (await SendAsync(channel, message, cancellationToken).ConfigureAwait(false) is { } problem)
                {
                    return problem;
                }
            }
        }
    }

    // Null once sent, otherwise why not.
    public static async Task<string?> SendAsync(ConsoleChannel channel, ConsoleMessage message, CancellationToken cancellationToken)
    {
        using CancellationTokenSource sending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sending.CancelAfter(SendTimeout);

        try
        {
            await channel.SendAsync(message, sending.Token).ConfigureAwait(false);

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"took no message for {Durations.Describe(SendTimeout)}";
        }
        catch (IOException exception)
        {
            return ConsoleFailure.ClosedPipe(exception);
        }
    }
}

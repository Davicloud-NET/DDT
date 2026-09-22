// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.E2E;

// Polls until something holds, and otherwise fails after a bound with what was awaited and what was seen.
internal static class Eventually
{
    private static readonly TimeSpan s_interval = TimeSpan.FromMilliseconds(100);

    public static async Task<T> GetAsync<T>(
        string what,
        TimeSpan timeout,
        Func<CancellationToken, Task<T?>> probe,
        Func<string> diagnostics,
        CancellationToken cancellationToken,
        TimeSpan? interval = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(diagnostics);

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);

        try
        {
            while (true)
            {
                if (await probe(limit.Token).ConfigureAwait(false) is { } value)
                {
                    return value;
                }

                await Task.Delay(interval ?? s_interval, limit.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{what} did not happen within {timeout.TotalSeconds:0} s.{Environment.NewLine}{diagnostics()}");
        }
    }

    public static Task WaitAsync(
        string what,
        TimeSpan timeout,
        Func<CancellationToken, Task<bool>> probe,
        Func<string> diagnostics,
        CancellationToken cancellationToken,
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(probe);

        return GetAsync(what, timeout, async call => await probe(call).ConfigureAwait(false) ? what : null, diagnostics, cancellationToken, interval);
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.E2E;

// Polls until something holds, and otherwise fails after a bound with what was awaited and what was seen.
internal static class Eventually
{
    public static async Task<T> GetAsync<T>(
        Expectation expectation,
        Func<CancellationToken, Task<T?>> probe,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(expectation);
        ArgumentNullException.ThrowIfNull(probe);

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(expectation.Timeout);

        try
        {
            while (true)
            {
                if (await probe(limit.Token).ConfigureAwait(false) is { } value)
                {
                    return value;
                }

                await Task.Delay(expectation.Interval, limit.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{expectation.What} did not happen within {expectation.Timeout.TotalSeconds:0} s.{Environment.NewLine}{expectation.Diagnostics()}");
        }
    }

    public static Task WaitAsync(Expectation expectation, Func<CancellationToken, Task<bool>> probe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectation);
        ArgumentNullException.ThrowIfNull(probe);

        return GetAsync(expectation, async call => await probe(call).ConfigureAwait(false) ? expectation.What : null, cancellationToken);
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Live;

// Sends at most one push per key and interval. The first goes out at once. Pushes during the interval become one push
// with the latest payload, sent when the interval ends. A push still waiting when the host stops is dropped.
public sealed class PushThrottle(TimeProvider timeProvider, TimeSpan interval, CancellationToken stopping)
{
    // Keys that pushed longer ago than the interval are forgotten once this many are remembered.
    private const int RememberedKeys = 1024;

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, DateTimeOffset> _sent = [];
    private readonly Dictionary<Guid, Func<Task>> _waiting = [];

    public void Push(Guid key, Func<Task> push)
    {
        ArgumentNullException.ThrowIfNull(push);

        TimeSpan wait;

        lock (_lock)
        {
            if (_waiting.ContainsKey(key))
            {
                _waiting[key] = push;

                return;
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            wait = _sent.TryGetValue(key, out DateTimeOffset last) ? last + interval - now : TimeSpan.Zero;

            if (wait > TimeSpan.Zero)
            {
                _waiting[key] = push;
            }
            else
            {
                Forget(now);
                _sent[key] = now;
            }
        }

        _ = wait > TimeSpan.Zero ? PushLaterAsync(key, wait, stopping) : push();
    }

    // Drops the push waiting for the key. Used when the thing it would push is gone.
    public void Discard(Guid key)
    {
        lock (_lock)
        {
            _waiting.Remove(key);
        }
    }

    private async Task PushLaterAsync(Guid key, TimeSpan wait, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Func<Task>? push;

        lock (_lock)
        {
            _waiting.Remove(key, out push);
            _sent[key] = timeProvider.GetUtcNow();
        }

        if (push is not null)
        {
            await push().ConfigureAwait(false);
        }
    }

    private void Forget(DateTimeOffset now)
    {
        if (_sent.Count < RememberedKeys)
        {
            return;
        }

        foreach (Guid key in _sent.Where(sent => now - sent.Value >= interval && !_waiting.ContainsKey(sent.Key)).Select(sent => sent.Key).ToList())
        {
            _sent.Remove(key);
        }
    }
}

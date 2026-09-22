// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Live;

// At most one push per key and interval: the first goes out at once, and whatever comes during the interval after it
// goes out as one push, with the latest payload, when the interval ends.
public sealed class PushThrottle(TimeProvider timeProvider, TimeSpan interval)
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

        _ = wait > TimeSpan.Zero ? PushLaterAsync(key, wait) : push();
    }

    private async Task PushLaterAsync(Guid key, TimeSpan wait)
    {
        await Task.Delay(wait, timeProvider).ConfigureAwait(false);

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

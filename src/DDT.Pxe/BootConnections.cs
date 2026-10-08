// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;

namespace DDT.Pxe;

// The boot port's open connections that carry the Linux boot environment, and the reads that ended badly, by machine.
public sealed class BootConnections(TimeProvider timeProvider, SystemTcpConnections? system = null)
{
    // How long after a read that ended badly a machine is kept off HTTP, whatever the server's system still holds.
    public static readonly TimeSpan Remembered = TimeSpan.FromMinutes(3);

    // A rollout of more machines than this within three minutes forgets the oldest of them.
    public const int MaxRemembered = 4096;

    private readonly Lock _lock = new();
    private readonly Dictionary<IPAddress, List<Registration>> _open = [];
    private readonly Dictionary<IPAddress, DateTimeOffset> _failed = [];

    // abort resets the connection. Dispose the registration when the connection has ended.
    public IDisposable Opened(IPAddress client, Action abort)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(abort);

        Registration registration = new(this, Key(client), abort);

        lock (_lock)
        {
            if (!_open.TryGetValue(registration.Client, out List<Registration>? connections))
            {
                _open[registration.Client] = connections = [];
            }

            connections.Add(registration);
        }

        return registration;
    }

    // A read over HTTP ended before its whole answer was sent.
    public void ReadFailed(IPAddress client)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (_lock)
        {
            Remember(Key(client));
        }
    }

    // The machine starts again. GRUB hangs on a connection its start before left here, so those are reset and counted.
    public int NetbootStarts(IPAddress client)
    {
        ArgumentNullException.ThrowIfNull(client);

        List<Registration>? left;

        lock (_lock)
        {
            if (!_open.Remove(Key(client), out left))
            {
                return 0;
            }

            // What a reset connection was sending did not arrive.
            Remember(Key(client));
        }

        foreach (Registration registration in left)
        {
            registration.Abort();
        }

        return left.Count;
    }

    // False while a read of the machine that ended badly is remembered.
    public bool HttpIsSafe(IPAddress client)
    {
        ArgumentNullException.ThrowIfNull(client);

        lock (_lock)
        {
            return !_failed.TryGetValue(Key(client), out DateTimeOffset failed) || IsOver(failed, timeProvider.GetUtcNow());
        }
    }

    // Why the machine's HTTP reads on the boot port could meet what its start before left on the server.
    public HttpBootRisk HttpRisk(IPAddress client, int bootPort)
    {
        if (!HttpIsSafe(client))
        {
            return HttpBootRisk.ReadEndedBadly;
        }

        // Kestrel no longer knows a connection it has closed, and cannot reset it. The system holds it for a while.
        return system?.Any(client, bootPort) == true ? HttpBootRisk.ConnectionLeft : HttpBootRisk.None;
    }

    // How many connections are open now, for a test and a log line.
    public int Open
    {
        get
        {
            lock (_lock)
            {
                return _open.Values.Sum(connections => connections.Count);
            }
        }
    }

    private static IPAddress Key(IPAddress client) => client.IsIPv4MappedToIPv6 ? client.MapToIPv4() : client;

    // A time in the future is left from before the clock was set back, and keeps no machine off HTTP.
    private static bool IsOver(DateTimeOffset failed, DateTimeOffset now) => failed > now || now - failed > Remembered;

    private void Remember(IPAddress client)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        if (_failed.Count >= MaxRemembered && !_failed.ContainsKey(client))
        {
            foreach (IPAddress old in _failed.Where(entry => IsOver(entry.Value, now)).Select(entry => entry.Key).ToList())
            {
                _failed.Remove(old);
            }

            if (_failed.Count >= MaxRemembered)
            {
                _failed.Remove(_failed.MinBy(entry => entry.Value).Key);
            }
        }

        _failed[client] = now;
    }

    private void Closed(Registration registration)
    {
        lock (_lock)
        {
            if (_open.TryGetValue(registration.Client, out List<Registration>? connections)
                && connections.Remove(registration)
                && connections.Count == 0)
            {
                _open.Remove(registration.Client);
            }
        }
    }

    private sealed class Registration(BootConnections owner, IPAddress client, Action abort) : IDisposable
    {
        public IPAddress Client { get; } = client;

        public void Abort() => abort();

        public void Dispose() => owner.Closed(this);
    }
}

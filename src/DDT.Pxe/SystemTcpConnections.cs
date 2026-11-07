// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.NetworkInformation;

namespace DDT.Pxe;

// The TCP connections the server's own system holds, in every state. One that Kestrel has closed stays there for a
// minute or more, waiting for the other side's close or in TIME_WAIT, and GRUB's next start meets it all the same.
public sealed class SystemTcpConnections(TimeProvider timeProvider, Func<TcpConnectionInformation[]>? read = null)
{
    // A start asks once, seconds after what it could meet was left. A flood of requests costs one reading a second.
    public static readonly TimeSpan Kept = TimeSpan.FromSeconds(1);

    private readonly Lock _lock = new();
    private readonly Func<TcpConnectionInformation[]> _read = read ?? (() => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections());
    private HashSet<(IPAddress Remote, int LocalPort)> _connections = [];
    private DateTimeOffset? _readAt;

    // Whether the system holds a connection between the remote address and the local port.
    public bool Any(IPAddress remote, int localPort)
    {
        ArgumentNullException.ThrowIfNull(remote);

        lock (_lock)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();

            if (_readAt is not { } readAt || now < readAt || now - readAt >= Kept)
            {
                _connections = Read();
                _readAt = now;
            }

            return _connections.Contains((Key(remote), localPort));
        }
    }

    private static IPAddress Key(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private HashSet<(IPAddress Remote, int LocalPort)> Read()
    {
        try
        {
            return [.. _read().Select(connection => (Key(connection.RemoteEndPoint.Address), connection.LocalEndPoint.Port))];
        }
        catch (Exception exception) when (exception is NetworkInformationException or PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
            // A system that does not say leaves GRUB to its own timeout and to the TFTP half of grub.cfg.
            return [];
        }
    }
}

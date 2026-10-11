// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;

namespace DDT.Agent;

// A host's addresses by its name, as the agent for Linux asks for them. Its resolver, musl's, gives a name up when
// the DNS server answers the IPv6 question with an error, though it answered the IPv4 one. The system's own tools
// find such a name, so a lookup that fails is asked again for IPv4 alone.
public sealed class NameLookup
{
    public Func<string, CancellationToken, Task<IPAddress[]>> Whole { get; init; } = Dns.GetHostAddressesAsync;

    public Func<string, CancellationToken, Task<IPAddress[]>> IPv4Alone { get; init; } =
        static (host, cancellationToken) => Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken);

    // Throws the whole lookup's SocketException when IPv4 alone finds nothing either. Empty for a name without an address.
    public async Task<IPAddress[]> AddressesAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            return await Whole(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            if (await IPv4OrNoneAsync(host, cancellationToken).ConfigureAwait(false) is { Length: > 0 } found)
            {
                return found;
            }

            throw;
        }
    }

    // The IPv4 addresses, and none where that lookup fails too.
    public async Task<IPAddress[]> IPv4OrNoneAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            return await IPv4Alone(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return [];
        }
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;

namespace DDT.Agent;

// Finds the server's addresses for a new connection. In an installed Linux the name may not be found though the boot
// environment reached the server: that one had the name from the server itself, and here a DNS is asked. So IPv4 is
// asked for alone, and then the address the boot environment used is taken. TLS checks the name either way.
public sealed class ServerLookup
{
    // How long the name's lookup may take when the address is known anyway. A DNS server that is not there says nothing.
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(4);

    private readonly Lock _lock = new();
    private string? _said;
    private IPAddress? _reachedAt;

    // What looks the name up. Null takes the system's answer as it is, as every agent but the one in Linux does.
    public NameLookup? Names { get; init; }

    // The address at which the boot environment reached the server, from the hand-over's agent.json.
    public IPAddress? Known { get; init; }

    public AgentLog? Log { get; init; }

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    // The address the newest connection reached the server at. Null before the first.
    public IPAddress? ReachedAt
    {
        get
        {
            lock (_lock)
            {
                return _reachedAt;
            }
        }
    }

    public static string IPv4AloneMessage(string host, string failure, IEnumerable<IPAddress> found) =>
        $"The name {host} could not be looked up ({failure}), but has an address when IPv4 alone is asked for: " +
        $"{string.Join(", ", found)}. The DNS server answers the IPv6 question with an error. The agent reaches the server there.";

    public static string KnownMessage(string host, string failure, IPAddress known) =>
        $"The name {host} cannot be looked up in this Linux ({failure}), so the agent reaches the server at {known}, where " +
        "the boot environment reached it. The server's certificate is still checked against the name.";

    public async Task<IPAddress[]> AddressesAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        if (IPAddress.TryParse(host, out IPAddress? literal))
        {
            return [literal];
        }

        if (Names is not { } names)
        {
            return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }

        Exception failure;

        try
        {
            if (await PatientlyAsync(names.Whole(host, cancellationToken), cancellationToken).ConfigureAwait(false) is { Length: > 0 } found)
            {
                return found;
            }

            failure = new SocketException((int)SocketError.NoData);
        }
        catch (Exception exception) when (exception is SocketException or TimeoutException)
        {
            failure = exception;
        }

        return await WithoutTheWholeLookupAsync(names, host, failure, cancellationToken).ConfigureAwait(false);
    }

    // Called once a connection stands, with the address it reached.
    public void Reached(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        lock (_lock)
        {
            _reachedAt = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        }
    }

    // A lookup that got no answer in time gets none for IPv4 either, so that question is only asked after an error.
    private async Task<IPAddress[]> WithoutTheWholeLookupAsync(NameLookup names, string host, Exception failure, CancellationToken cancellationToken)
    {
        string why = failure is TimeoutException ? $"no answer within {Durations.Describe(Patience)}" : failure.Message;

        if (failure is SocketException
            && await PatientlyOrNoneAsync(names.IPv4OrNoneAsync(host, cancellationToken), cancellationToken).ConfigureAwait(false) is { Length: > 0 } found)
        {
            SayOnce(IPv4AloneMessage(host, why, found));

            return found;
        }

        if (Known is { } known)
        {
            SayOnce(KnownMessage(host, why, known));

            return [known];
        }

        ExceptionDispatchInfo.Throw(failure);

        return [];
    }

    // Without a known address there is nothing to fall back on, and the connection's own time limit ends the lookup.
    private Task<IPAddress[]> PatientlyAsync(Task<IPAddress[]> lookup, CancellationToken cancellationToken) =>
        Known is null ? lookup : lookup.WaitAsync(Patience, TimeProvider, cancellationToken);

    private async Task<IPAddress[]> PatientlyOrNoneAsync(Task<IPAddress[]> lookup, CancellationToken cancellationToken)
    {
        try
        {
            return await PatientlyAsync(lookup, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return [];
        }
    }

    // Every new connection asks, so the line is said when the way changes and not for each.
    private void SayOnce(string line)
    {
        lock (_lock)
        {
            if (_said == line)
            {
                return;
            }

            _said = line;
        }

        Log?.Warning(line);
    }
}

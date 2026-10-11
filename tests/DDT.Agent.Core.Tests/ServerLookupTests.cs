// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using System.Text;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

// How the agent in an installed Linux finds the server when the name's lookup fails, with a resolver that answers as
// musl's did on two real images: the whole name is unknown once the DNS server answers the IPv6 question with an error.
public sealed class ServerLookupTests : IDisposable
{
    private const string Host = "ddt-lab.m8p.test";

    private static readonly IPAddress s_address = IPAddress.Parse("10.77.0.1");

    private readonly StringWriter _console = new();
    private readonly AgentLog _log;
    private readonly ManualTimeProvider _time = new();
    private readonly List<string> _asked = [];

    public ServerLookupTests() => _log = new AgentLog(new ImmediateTimeProvider(), _console);

    public void Dispose() => _console.Dispose();

    private string[] Lines() =>
        [.. _console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[9..])];

    private static Task<IPAddress[]> Unknown() => Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound));

    // whole and ipv4 answer the two questions. Null means that the name is not found.
    private ServerLookup Lookup(Func<Task<IPAddress[]>>? whole, Func<Task<IPAddress[]>>? ipv4, IPAddress? known = null) => new()
    {
        Names = new NameLookup
        {
            Whole = (host, _) =>
            {
                _asked.Add($"{host} whole");

                return whole?.Invoke() ?? Unknown();
            },
            IPv4Alone = (host, _) =>
            {
                _asked.Add($"{host} IPv4");

                return ipv4?.Invoke() ?? Unknown();
            },
        },
        Known = known,
        Log = _log,
        TimeProvider = _time,
    };

    private Task<IPAddress[]> AddressesAsync(ServerLookup lookup) => lookup.AddressesAsync(Host, TestContext.Current.CancellationToken);

    [Fact]
    public async Task TakesTheNamesAddressesAsTheyAreAndSaysNothing()
    {
        IPAddress[] found = [IPAddress.Parse("fd00::17"), s_address];

        Assert.Equal(found, await AddressesAsync(Lookup(() => Task.FromResult(found), null, IPAddress.Parse("10.9.9.9"))));

        Assert.Equal([$"{Host} whole"], _asked);
        Assert.Empty(Lines());
    }

    // What the proof ran into: an A record, and "no such name" or "refused" for the AAAA question.
    [Fact]
    public async Task AsksForIPv4AloneWhenTheWholeLookupFailsAndSaysSoOnce()
    {
        ServerLookup lookup = Lookup(null, () => Task.FromResult<IPAddress[]>([s_address]), IPAddress.Parse("10.9.9.9"));

        Assert.Equal([s_address], await AddressesAsync(lookup));
        Assert.Equal([s_address], await AddressesAsync(lookup));

        Assert.Equal([$"{Host} whole", $"{Host} IPv4", $"{Host} whole", $"{Host} IPv4"], _asked);
        string line = Assert.Single(Lines());
        Assert.StartsWith($"WARN  The name {Host} could not be looked up (", line, StringComparison.Ordinal);
        Assert.EndsWith(
            "but has an address when IPv4 alone is asked for: 10.77.0.1. The DNS server answers the IPv6 question with an error. " +
            "The agent reaches the server there.",
            line,
            StringComparison.Ordinal);
    }

    // The installed Linux asks a DNS that does not know the server at all. The boot environment had the name and the
    // address from the server itself, and the hand-over passed the address on.
    [Fact]
    public async Task TakesTheAddressTheBootEnvironmentUsedWhenNoLookupFindsTheName()
    {
        ServerLookup lookup = Lookup(null, null, s_address);

        Assert.Equal([s_address], await AddressesAsync(lookup));
        Assert.Equal([s_address], await AddressesAsync(lookup));

        string line = Assert.Single(Lines());
        Assert.StartsWith($"WARN  The name {Host} cannot be looked up in this Linux (", line, StringComparison.Ordinal);
        Assert.EndsWith(
            "so the agent reaches the server at 10.77.0.1, where the boot environment reached it. The server's certificate is still " +
            "checked against the name.",
            line,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameThatHasNoAddressIsLookedForTheOtherWaysToo()
    {
        ServerLookup lookup = Lookup(() => Task.FromResult<IPAddress[]>([]), null, s_address);

        Assert.Equal([s_address], await AddressesAsync(lookup));
        Assert.Equal([$"{Host} whole", $"{Host} IPv4"], _asked);
    }

    // An agent.json of a hand-over before this one names no address, and the failure is the lookup's own as before.
    [Fact]
    public async Task WithoutAKnownAddressTheLookupsOwnFailureIsThrown()
    {
        SocketException thrown = new((int)SocketError.TryAgain);
        ServerLookup lookup = Lookup(() => Task.FromException<IPAddress[]>(thrown), null);

        SocketException failure = await Assert.ThrowsAsync<SocketException>(() => AddressesAsync(lookup));

        Assert.Same(thrown, failure);
        Assert.Empty(Lines());
    }

    // A DNS server that is not there answers nothing, for longer than a connection may take to open.
    [Fact]
    public async Task DoesNotWaitLongForALookupWhenItKnowsTheAddressAnyway()
    {
        TaskCompletionSource<IPAddress[]> never = new();
        ServerLookup lookup = Lookup(() => never.Task, () => never.Task, s_address);

        Task<IPAddress[]> looking = AddressesAsync(lookup);
        await _time.AdvanceUntilAsync(TimeSpan.FromSeconds(1), () => looking.IsCompleted);

        Assert.Equal([s_address], await looking);
        Assert.Equal([$"{Host} whole"], _asked);
        Assert.Contains("(no answer within 4 s)", Assert.Single(Lines()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAddressInPlaceOfANameNeedsNoLookup()
    {
        ServerLookup lookup = Lookup(null, null, s_address);

        Assert.Equal([IPAddress.Parse("10.0.100.200")], await lookup.AddressesAsync("10.0.100.200", TestContext.Current.CancellationToken));
        Assert.Empty(_asked);
    }

    // The names stand apart from the server: a step's share is looked up the same way, without a known address.
    [Fact]
    public async Task AShareHostsNameIsAskedAgainForIPv4AloneToo()
    {
        NameLookup names = new()
        {
            Whole = (_, _) => Unknown(),
            IPv4Alone = (host, _) => host == "files.m8p.test" ? Task.FromResult<IPAddress[]>([s_address]) : Unknown(),
        };

        Assert.Equal([s_address], await names.AddressesAsync("files.m8p.test", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<SocketException>(() => names.AddressesAsync("nowhere.m8p.test", TestContext.Current.CancellationToken));
    }

    // The whole way of a request: the name is found nowhere, the connection goes to the known address, and the
    // request still names the server by its name, which is what its certificate is checked against.
    [Fact]
    public async Task AConnectionGoesToTheKnownAddressAndStillNamesTheServer()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        ServerLookup lookup = Lookup(null, null, IPAddress.Loopback);
        using HttpAgentServer server = new(new Uri($"http://{Host}:{port}/"), null) { Lookup = lookup };

        Task<AgentRelease?> request = server.GetReleaseAsync(cancellationToken);
        using TcpClient accepted = await listener.AcceptTcpClientAsync(cancellationToken);
        NetworkStream stream = accepted.GetStream();
        byte[] buffer = new byte[4096];
        int read = 0;

        while (!Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            read += await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
        }

        byte[] body = """{"sha256":"00","size":1}"""u8.ToArray();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n"), cancellationToken);
        await stream.WriteAsync(body, cancellationToken);

        Assert.NotNull(await request);
        Assert.Contains($"Host: {Host}:{port}\r\n", Encoding.ASCII.GetString(buffer, 0, read), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(IPAddress.Loopback, lookup.ReachedAt);
        Assert.Single(Lines());
    }
}

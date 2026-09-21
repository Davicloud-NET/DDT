// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using DDT.Contracts.Authentication;
using DDT.Server.Data;
using DDT.Server.Machines;
using DDT.Server.Security;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ForwardedHeadersTests(ProxiedApplication application, ForwardedHeadersSwitchApplication unconfigured)
    : IClassFixture<ProxiedApplication>, IClassFixture<ForwardedHeadersSwitchApplication>
{
    private const string ForwardedFor = "X-Forwarded-For";
    private const string ForwardedProto = "X-Forwarded-Proto";
    private const string WrongPassword = "Not the right password 7";
    private const int SignInLimit = 10;

    [Theory]
    [InlineData(ProxiedApplication.Proxy, "203.0.113.1", "203.0.113.1")]
    [InlineData(ProxiedApplication.Ipv6Proxy, "203.0.113.2", "203.0.113.2")]
    [InlineData("::ffff:" + ProxiedApplication.Proxy, "203.0.113.3", "203.0.113.3")]
    [InlineData("198.51.100.7", "203.0.113.4", "203.0.113.4")]
    [InlineData(ProxiedApplication.Proxy, "192.0.2.99, 203.0.113.5", "203.0.113.5")]
    [InlineData(ProxiedApplication.Proxy, "192.0.2.99, 198.51.100.8", "198.51.100.8")]
    [InlineData("203.0.113.6", "192.0.2.98", "203.0.113.6")]
    [InlineData("127.0.0.1", "192.0.2.97", "127.0.0.1")]
    [InlineData("::1", "192.0.2.96", "::1")]
    [InlineData(null, "192.0.2.94", null)]
    public async Task MachinesAreRecordedAtTheAddressTheProxyReports(string? connection, string forwardedFor, string? expected)
    {
        Assert.Equal(expected, await RecordedAddressAsync(application, connection, forwardedFor));
    }

    [Fact]
    public async Task TheFrameworkSwitchAloneTrustsNoAddress()
    {
        Assert.Equal("203.0.113.7", await RecordedAddressAsync(unconfigured, "203.0.113.7", "192.0.2.95"));
    }

    // The switch adds the middleware too, and two of them would take the client's entry left of a listed address.
    [Fact]
    public async Task TheFrameworkSwitchNextToAListedProxyStillReadsOneEntry()
    {
        using SettingsApplication both = new(
            ("ForwardedHeaders_Enabled", "true"),
            ("DDT:ForwardedHeaders:KnownProxies", ProxiedApplication.Proxy),
            ("DDT:ForwardedHeaders:KnownNetworks", ProxiedApplication.ProxyNetwork));

        Assert.Equal("198.51.100.8", await RecordedAddressAsync(both, ProxiedApplication.Proxy, "192.0.2.99, 198.51.100.8"));
    }

    // A connection with no address, as over a Unix socket or a named pipe, cannot be a listed one.
    [Fact]
    public async Task TheFrameworkSwitchNextToAListedProxyIgnoresAConnectionWithoutAnAddress()
    {
        using SettingsApplication both = new(
            ("ForwardedHeaders_Enabled", "true"),
            ("DDT:ForwardedHeaders:KnownProxies", ProxiedApplication.Proxy));

        Assert.Null(await RecordedAddressAsync(both, null, "192.0.2.93"));
    }

    [Fact]
    public async Task EachClientBehindTheProxyHasItsOwnSignInLimit()
    {
        string client = TestRemoteAddress.Unique();

        for (int attempt = 0; attempt < SignInLimit; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(ProxiedApplication.Proxy, client));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await SignInAsync(ProxiedApplication.Proxy, client));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(ProxiedApplication.Proxy, TestRemoteAddress.Unique()));
    }

    [Fact]
    public async Task AClientThatIsNotAProxyCannotLeaveItsSignInLimit()
    {
        string client = TestRemoteAddress.Unique();

        for (int attempt = 0; attempt < SignInLimit; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(client, TestRemoteAddress.Unique()));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await SignInAsync(client, TestRemoteAddress.Unique()));
    }

    [Theory]
    [InlineData(ProxiedApplication.Proxy, true)]
    [InlineData("203.0.113.8", false)]
    [InlineData(null, false)]
    public async Task ForwardedHttpsCountsOnlyFromAProxy(string? connection, bool fromProxy)
    {
        using HttpClient http = application.CreateDefaultClient();

        if (connection is not null)
        {
            http.DefaultRequestHeaders.Add(TestRemoteAddress.Header, connection);
        }

        http.DefaultRequestHeaders.Add(ForwardedFor, TestRemoteAddress.Unique());
        http.DefaultRequestHeaders.Add(ForwardedProto, "https");

        using HttpResponseMessage session = await http.GetAsync(
            new Uri("/api/auth/session", UriKind.Relative),
            TestContext.Current.CancellationToken);
        SetCookieHeaderValue antiforgery = SetCookieHeaderValue.ParseList([.. session.Headers.GetValues(HeaderNames.SetCookie)]).Single();

        Assert.Equal(fromProxy, antiforgery.Secure);

        // Without Sec-Fetch-Site the same origin filter compares Origin with the request's own scheme and host.
        using HttpRequestMessage login = new(HttpMethod.Post, new Uri("/api/auth/login", UriKind.Relative))
        {
            Content = JsonContent.Create(UnknownAccount(), options: TestJson.Options),
        };

        login.Headers.Add(HeaderNames.Origin, "https://localhost");
        login.Headers.Add(HeaderNames.Cookie, $"{antiforgery.Name}={antiforgery.Value}");
        login.Headers.Add(CsrfHeaderNames.RequestToken, session.Headers.GetValues(CsrfHeaderNames.RequestToken).Single());

        using HttpResponseMessage response = await http.SendAsync(login, TestContext.Current.CancellationToken);

        Assert.Equal(fromProxy ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Registration records the address twice, on the machine and in the audit table.
    private static async Task<string?> RecordedAddressAsync(DdtApplication host, string? connection, string forwardedFor)
    {
        HttpClient http = host.CreateDefaultClient();
        http.DefaultRequestHeaders.Add(ForwardedFor, forwardedFor);

        using RegisteredMachine registered = await host.RegisterMachineAsync(new AgentClient(http, connection));

        using IServiceScope scope = host.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string subject = registered.Id.ToString("D");

        Machine machine = await database.Machines.SingleAsync(m => m.Id == registered.Id, cancellationToken);
        AuditEvent audit = await database.AuditEvents
            .SingleAsync(e => e.SubjectId == subject && e.Action == AuditActions.MachineRegistered, cancellationToken);

        Assert.Equal(machine.LastSeenAddress, audit.SourceAddress);

        return machine.LastSeenAddress;
    }

    private static LoginRequest UnknownAccount() => new("nobody-" + Guid.NewGuid().ToString("N"), WrongPassword, null, null);

    private async Task<HttpStatusCode> SignInAsync(string connection, string forwardedFor)
    {
        CookieContainer cookies = new();
        HttpClient http = application.CreateDefaultClient(new CookieContainerHandler(cookies));
        http.DefaultRequestHeaders.Add(TestRemoteAddress.Header, connection);
        http.DefaultRequestHeaders.Add(ForwardedFor, forwardedFor);

        using SignedInClient client = new(http, cookies);
        using HttpResponseMessage response = await client.PostAsync("/api/auth/login", UnknownAccount());

        return response.StatusCode;
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DDT.Server.Tests;

// The framework behaviour the settings rely on, each proved here rather than assumed.
public sealed class SettingsFrameworkTests
{
    // The settings service loads the store as it starts, and the first request must already see what it loaded.
    [Fact]
    public async Task EveryHostedServiceFinishesStartingBeforeKestrelListens()
    {
        int port = FreePort();
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{port}");
        Probe probe = new(port);
        builder.Services.AddHostedService(_ => probe);

        await using WebApplication app = builder.Build();
        await app.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(probe.ConnectedWhileStarting);
        Assert.True(await Connects(port));

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    // The monitor drops the options it cached when the change token of the snapshot fires, and builds them again from the
    // new snapshot at once, whether or not anything listens.
    [Fact]
    public void TheOptionsOfTheOidcSchemeFollowTheSnapshot()
    {
        using ServiceProvider services = Services(collection =>
        {
            collection.AddSingleton<OidcSchemeBridge>();
            collection.AddSingleton<IConfigureOptions<OpenIdConnectOptions>>(provider => provider.GetRequiredService<OidcSchemeBridge>());
            collection.AddSingleton<IOptionsChangeTokenSource<OpenIdConnectOptions>>(provider => provider.GetRequiredService<OidcSchemeBridge>());
        });
        IOptionsMonitor<OpenIdConnectOptions> monitor = services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();
        DdtSettings settings = services.GetRequiredService<DdtSettings>();

        Assert.Equal(string.Empty, monitor.Get(OidcOptions.SchemeName).ClientId);

        settings.Publish([SettingsSnapshotTests.Stored(SettingsSectionNames.Oidc, """{"clientId":"ddt","scopes":["openid","groups"]}""")]);

        OpenIdConnectOptions options = monitor.Get(OidcOptions.SchemeName);
        Assert.Equal("ddt", options.ClientId);
        Assert.Equal(["openid", "groups"], options.Scope);
        Assert.Equal(Microsoft.AspNetCore.Identity.IdentityConstants.ExternalScheme, options.SignInScheme);
    }

    // The logger factory takes new filter rules when its options monitor reports a change.
    [Fact]
    public void LogLevelsFollowTheSnapshot()
    {
        using RecordingLoggerProvider recording = new();
        using ServiceProvider services = Services(collection =>
        {
            collection.AddLogging(logging => logging.AddProvider(recording));
            collection.AddSingleton<LoggingSettingsBridge>();
            collection.AddSingleton<IConfigureOptions<LoggerFilterOptions>>(provider => provider.GetRequiredService<LoggingSettingsBridge>());
            collection.AddSingleton<IOptionsChangeTokenSource<LoggerFilterOptions>>(provider => provider.GetRequiredService<LoggingSettingsBridge>());
        });
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DDT.Settings.Probe");

        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.True(logger.IsEnabled(LogLevel.Information));

        services.GetRequiredService<DdtSettings>().Publish(
            [SettingsSnapshotTests.Stored(SettingsSectionNames.Logging, """{"logLevel":{"Default":"Warning","DDT.Settings":"Debug"}}""")]);

        Assert.True(logger.IsEnabled(LogLevel.Debug));
        Assert.False(services.GetRequiredService<ILoggerFactory>().CreateLogger("Other").IsEnabled(LogLevel.Information));
    }

    // ForwardedHeadersMiddleware.ApplyForwarders is public, so the proxies can change while the server runs: a proxy
    // saved on the page is believed from the next request on, and one removed is not.
    [Fact]
    public async Task AProxySavedOnThePageIsBelievedFromTheNextRequest()
    {
        using DdtApplication application = new();
        SignedInClient administrator = await application.AdministratorAsync();
        string token = await administrator.TokenAsync();

        Assert.Equal("192.0.2.77", await RecordedAddressAsync(application, "198.51.100.44"));

        await administrator.SavedAsync<ProxySettings>(SettingsSectionNames.Proxies, values => values with { KnownProxies = ["192.0.2.77"] }, reauthentication: token);

        Assert.Equal("198.51.100.44", await RecordedAddressAsync(application, "198.51.100.44"));
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, application.Services.GetRequiredService<DdtSettings>().Current.ForwardedHeaders.ForwardedHeaders);

        await administrator.SavedAsync<ProxySettings>(SettingsSectionNames.Proxies, values => values with { KnownProxies = [] }, reauthentication: token);

        Assert.Equal("192.0.2.77", await RecordedAddressAsync(application, "198.51.100.44"));
    }

    private static async Task<string?> RecordedAddressAsync(DdtApplication application, string forwardedFor)
    {
        HttpClient http = application.CreateDefaultClient();
        http.DefaultRequestHeaders.Add(ForwardedHeadersDefaults.XForwardedForHeaderName, forwardedFor);

        using RegisteredMachine registered = await application.RegisterMachineAsync(new AgentClient(http, "192.0.2.77"));

        return (await application.MachineAsync(registered.Id)).LastSeenAddress;
    }

    private static ServiceProvider Services(Action<IServiceCollection> configure)
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<DdtSettings>();
        services.AddOptions();
        configure(services);

        return services.BuildServiceProvider();
    }

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<bool> Connects(int port)
    {
        using TcpClient client = new();

        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private sealed class Probe(int port) : IHostedService
    {
        public bool ConnectedWhileStarting { get; private set; } = true;

        public async Task StartAsync(CancellationToken cancellationToken) => ConnectedWhileStarting = await Connects(port);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

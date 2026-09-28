// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using DDT.Server.Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DDT.Server.Tests;

// What DDT relies on Kestrel for, proven on a real Kestrel with the extension the host uses.
public sealed class ServerCertificateKestrelTests : IDisposable
{
    // What Kestrel logs, at Debug and at Information, when it loads its configuration again and when it rebinds.
    private const string Reloading = "Config reload token fired";
    private const string Rebinding = "Config changed. Stopping the following endpoints";

    private static readonly string[] s_names = ["localhost", "127.0.0.1"];

    // Kestrel logs its rebind about four seconds after the file changed.
    private static readonly TimeSpan s_rebindTimeout = TimeSpan.FromSeconds(30);

    private readonly CertificateFolder _folder = new();
    private readonly ManualTimeProvider _clock = new();

    [Fact]
    public async Task TheCertificateInMemoryWinsOverKestrelsDefaultCertificate()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ServerCertificates certificates = await CertificatesAsync(cancellationToken);
        string served = certificates.Current!.SerialNumber;

        // Another pair from the same root is what Kestrel itself loads from Kestrel:Certificates:Default.
        string onDisk = WriteAnotherPair();

        await using WebApplication app = await StartAsync(builder => builder.AddDdtServerCertificates(certificates), cancellationToken);
        using TlsProbe probe = new(Address(app));

        Assert.Equal(served, await probe.GetAsync(cancellationToken));
        Assert.NotEqual(onDisk, served);
    }

    [Fact]
    public async Task ANewConnectionGetsTheRenewedCertificateWithoutARestart()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ServerCertificates certificates = await CertificatesAsync(cancellationToken);
        await using WebApplication app = await StartAsync(builder => builder.AddDdtServerCertificates(certificates), cancellationToken);

        using TlsProbe before = new(Address(app));
        string first = await before.GetAsync(cancellationToken);

        await RenewAsync(certificates, cancellationToken);

        using TlsProbe after = new(Address(app));
        string renewed = await after.GetAsync(cancellationToken);

        Assert.NotEqual(first, renewed);
        Assert.Equal(certificates.Current!.SerialNumber, renewed);
    }

    [Fact]
    public async Task AnOpenConnectionSurvivesARenewal()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ServerCertificates certificates = await CertificatesAsync(cancellationToken);
        await using WebApplication app = await StartAsync(builder => builder.AddDdtServerCertificates(certificates), cancellationToken);

        using TlsProbe probe = new(Address(app));
        string first = await probe.GetAsync(cancellationToken);

        await RenewAsync(certificates, cancellationToken);

        Assert.Equal(first, await probe.GetAsync(cancellationToken));
        Assert.Equal(1, probe.Handshakes);
    }

    // Kestrel watches the files of Kestrel:Certificates:Default, loads its configuration again when one changes, and
    // rebinds an endpoint that uses them, dropping its connections. A second Kestrel with the framework default on the
    // same files shows that it does.
    [Fact]
    public async Task ARenewalMakesKestrelNeitherReloadNorRebind()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ServerCertificates certificates = await CertificatesAsync(cancellationToken);
        using RecordingLoggerProvider ddtLog = new();
        using RecordingLoggerProvider defaultLog = new();

        await using WebApplication app = await StartAsync(
            builder =>
            {
                builder.AddDdtServerCertificates(certificates);
                builder.Logging.AddProvider(ddtLog);
                builder.Logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Debug);
            },
            cancellationToken);
        await using WebApplication control = await StartAsync(builder => builder.Logging.AddProvider(defaultLog), cancellationToken);

        Uri address = Address(app);
        using TlsProbe probe = new(address);
        await probe.GetAsync(cancellationToken);

        await RenewAsync(certificates, cancellationToken);
        await defaultLog.WaitForAsync(Rebinding, s_rebindTimeout, cancellationToken);

        Assert.DoesNotContain(ddtLog.Messages, message => message.Contains(Reloading, StringComparison.Ordinal));
        Assert.Equal(address, Address(app));
        await probe.GetAsync(cancellationToken);
        Assert.Equal(1, probe.Handshakes);
    }

    // The agent downloads no intermediates, so a certificate from an administrator's intermediate CA reaches it only
    // when the server sends the intermediate from the certificate file.
    [Fact]
    public async Task AnAdministratorsIntermediateGoesOutWithTheCertificate()
    {
        Assert.SkipWhen(
            OperatingSystem.IsWindows(),
            "On Windows .NET adds intermediates the machine cannot chain to the user's or the machine's certificate store, " +
            "which a test must not change. Run it on Linux, for example in the .NET SDK container.");

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PemPair root = AdministratorCertificate.CreateAuthority("CN=Example Root CA", null, now.AddDays(-2), now.AddYears(5));
        PemPair intermediate = AdministratorCertificate.CreateAuthority("CN=Example Issuing CA", root, now.AddDays(-2), now.AddYears(2));
        PemPair own = AdministratorCertificate.Issue(intermediate, "CN=localhost", s_names, now.AddDays(-1), now.AddDays(90));
        CertificateFolder.Write(_folder.Files, own with { CertificatePem = own.CertificatePem + "\n" + intermediate.CertificatePem });

        ServerCertificates certificates = new(_folder.Files, s_names, generate: false, _clock);
        await certificates.CheckAsync(cancellationToken);
        await using WebApplication app = await StartAsync(builder => builder.AddDdtServerCertificates(certificates), cancellationToken);

        using X509Certificate2 pin = X509Certificate2.CreateFromPem(root.CertificatePem);
        using HttpClient agent = AgentClient(Address(app), pin);

        Assert.Equal("ok", await agent.GetStringAsync(new Uri("/", UriKind.Relative), cancellationToken));
    }

    public void Dispose() => _folder.Dispose();

    // Trusts the pinned root and nothing else, downloads nothing and checks no revocation, like the agent.
    private static HttpClient AgentClient(Uri address, X509Certificate2 pin)
    {
        SocketsHttpHandler handler = new();
        handler.SslOptions.CertificateChainPolicy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
            CustomTrustStore = { pin },
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, errors) => errors == SslPolicyErrors.None;

        return new HttpClient(handler) { BaseAddress = address };
    }

    private async Task<ServerCertificates> CertificatesAsync(CancellationToken cancellationToken)
    {
        ServerCertificates certificates = new(_folder.Files, s_names, generate: true, _clock);
        await certificates.CheckAsync(cancellationToken);

        return certificates;
    }

    // The files are replaced the way a renewal always does it, by renaming them into place.
    private async Task RenewAsync(ServerCertificates certificates, CancellationToken cancellationToken)
    {
        _clock.Advance(TimeSpan.FromDays(61));

        Assert.Equal(CertificateAction.Renewed, (await certificates.CheckAsync(cancellationToken)).Action);
    }

    // Returns the serial number of the pair now on disk.
    private string WriteAnotherPair()
    {
        PemPair root = new(File.ReadAllText(_folder.Files.RootPath), File.ReadAllText(_folder.Files.RootKeyPath));
        PemPair other = ServerCertificateAuthority.Issue(root, s_names, [], _clock.GetUtcNow());

        File.WriteAllText(_folder.Files.KeyPath, other.KeyPem);
        File.WriteAllText(_folder.Files.CertificatePath, other.CertificatePem);

        using X509Certificate2 certificate = X509Certificate2.CreateFromPem(other.CertificatePem);

        return certificate.SerialNumber;
    }

    // The renewer the extension adds runs on the real clock, so it stays out of the way of the checks a test makes.
    private async Task<WebApplication> StartAsync(Action<WebApplicationBuilder> configure, CancellationToken cancellationToken)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = _folder.Path,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Https:Url"] = "https://127.0.0.1:0",
            ["Kestrel:Certificates:Default:Path"] = _folder.Files.CertificatePath,
            ["Kestrel:Certificates:Default:KeyPath"] = _folder.Files.KeyPath,
        });
        builder.Logging.ClearProviders();
        configure(builder);

        WebApplication app = builder.Build();
        app.MapGet("/", () => "ok");
        await app.StartAsync(cancellationToken);

        return app;
    }

    private static Uri Address(WebApplication app) => new(app.Urls.Single());
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography.X509Certificates;
using DDT.Server.Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DDT.Server.Tests;

// A pair the settings page installs is served at once but provisionally: unless it is confirmed from a connection that
// was served it, within 5 minutes, DDT goes back to the pair before it, files and all.
public sealed class ProvisionalCertificateTests : IDisposable
{
    private static readonly string[] s_names = ["localhost", "127.0.0.1"];

    private readonly CertificateFolder _folder = new();
    private readonly ManualTimeProvider _clock = new();

    [Fact]
    public async Task AnUnconfirmedPairGoesBackToThePairBefore()
    {
        ServerCertificates certificates = await CertificatesAsync();
        string before = certificates.Current!.Thumbprint;
        List<CertificateRolledBackEventArgs> rolledBack = [];
        certificates.RolledBack += (_, args) => rolledBack.Add(args);

        CertificateCheck installed = await certificates.GenerateAsync(s_names, TestContext.Current.CancellationToken);

        Assert.Equal(CertificateAction.Installed, installed.Action);
        Assert.Equal(installed.Certificate.Thumbprint, certificates.Current!.Thumbprint);
        Assert.Equal(_clock.GetUtcNow() + ServerCertificates.ConfirmWithin, certificates.Provisional!.DeadlineUtc);
        Assert.True(File.Exists(_folder.Files.PreviousCertificatePath));
        Assert.True(File.Exists(_folder.Files.ProvisionalPath));

        _clock.Advance(ServerCertificates.ConfirmWithin);
        await Eventually(() => certificates.Provisional is null);

        Assert.Equal(before, certificates.Current!.Thumbprint);
        Assert.Equal(before, _folder.Certificate().Thumbprint);
        Assert.False(File.Exists(_folder.Files.ProvisionalPath));
        Assert.Equal(installed.Certificate.Thumbprint, Assert.Single(rolledBack).RolledBackThumbprint);
    }

    // The confirmation proves that a browser accepted the new pair, so only a connection that was served it counts.
    [Fact]
    public async Task OnlyAConnectionServedTheNewPairConfirmsIt()
    {
        ServerCertificates certificates = await CertificatesAsync();
        string before = certificates.Current!.Thumbprint;
        string installed = (await certificates.GenerateAsync(s_names, TestContext.Current.CancellationToken)).Certificate.Thumbprint;

        Assert.Equal(CertificateConfirmation.NotServedTheNewPair, await certificates.ConfirmAsync(before, TestContext.Current.CancellationToken));
        Assert.Equal(CertificateConfirmation.NotServedTheNewPair, await certificates.ConfirmAsync(null, TestContext.Current.CancellationToken));
        Assert.Equal(CertificateConfirmation.Confirmed, await certificates.ConfirmAsync(installed, TestContext.Current.CancellationToken));
        Assert.Equal(CertificateConfirmation.NothingToConfirm, await certificates.ConfirmAsync(installed, TestContext.Current.CancellationToken));

        _clock.Advance(ServerCertificates.ConfirmWithin * 2);

        Assert.Equal(installed, certificates.Current!.Thumbprint);
        Assert.False(File.Exists(_folder.Files.ProvisionalPath));
    }

    // Several provisional pairs in a row go back to the one confirmed last, not to one another.
    [Fact]
    public async Task ARollbackGoesBackToThePairConfirmedLast()
    {
        ServerCertificates certificates = await CertificatesAsync();
        string confirmed = certificates.Current!.Thumbprint;

        await certificates.GenerateAsync(s_names, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(2));
        await certificates.GenerateAsync(s_names, TestContext.Current.CancellationToken);
        _clock.Advance(ServerCertificates.ConfirmWithin);
        await Eventually(() => certificates.Provisional is null);

        Assert.Equal(confirmed, certificates.Current!.Thumbprint);
    }

    // The deadline is kept in a file: a restart before it keeps waiting, and one after it goes back at once.
    [Fact]
    public async Task ARestartDoesNotMakeAProvisionalPairPermanent()
    {
        // Each process has a clock of its own here, so only the one a test advances acts.
        ServerCertificates first = await CertificatesAsync();
        string before = first.Current!.Thumbprint;
        string installed = (await first.GenerateAsync(s_names, TestContext.Current.CancellationToken)).Certificate.Thumbprint;

        ServerCertificates early = new(_folder.Files, s_names, generate: true, new ManualTimeProvider());
        await early.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(installed, early.Provisional?.Thumbprint);
        Assert.Equal(installed, early.Current!.Thumbprint);

        ManualTimeProvider later = new();
        later.Advance(ServerCertificates.ConfirmWithin + TimeSpan.FromMinutes(1));
        ServerCertificates restarted = new(_folder.Files, s_names, generate: true, later);
        await restarted.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Null(restarted.Provisional);
        Assert.Equal(before, restarted.Current!.Thumbprint);
        Assert.Equal(before, _folder.Certificate().Thumbprint);
        Assert.False(File.Exists(_folder.Files.ProvisionalPath));
    }

    // A pair half replaced by hand, a certificate with a key that is not its own, gives way to the previous pair at startup.
    [Fact]
    public async Task AtStartupAPairThatDoesNotLoadGivesWayToThePreviousOne()
    {
        ServerCertificates first = await CertificatesAsync();
        string before = first.Current!.Thumbprint;
        await first.GenerateAsync(s_names, TestContext.Current.CancellationToken);
        Assert.Equal(CertificateConfirmation.Confirmed, await first.ConfirmAsync(first.Current!.Thumbprint, TestContext.Current.CancellationToken));

        File.WriteAllText(_folder.Files.KeyPath, File.ReadAllText(_folder.Files.RootKeyPath));

        ServerCertificates restarted = new(_folder.Files, s_names, generate: false, _clock);
        await restarted.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(before, restarted.Current!.Thumbprint);
    }

    // Kestrel hands each connection the pair of the moment and the page learns which one it was; during a provisional
    // pair, a connection served the one before is closed after its answer, so the next request gets the new pair.
    [Fact]
    public async Task AConnectionServedThePairBeforeIsClosedWhileANewOneWaits()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ServerCertificates certificates = await CertificatesAsync();
        await using WebApplication app = await StartAsync(certificates);
        using TlsProbe probe = new(new Uri(app.Urls.Single()));

        string before = await probe.GetAsync(cancellationToken);
        Assert.Equal(1, probe.Handshakes);

        X509Certificate2 installed = (await certificates.GenerateAsync(s_names, cancellationToken)).Certificate;

        // The answer on the old connection closes it; the next one connects anew and gets the new pair.
        Assert.Equal(before, await probe.GetAsync(cancellationToken));
        Assert.Equal(installed.SerialNumber, await probe.GetAsync(cancellationToken));
        Assert.Equal(2, probe.Handshakes);
        Assert.Equal(installed.SerialNumber, await probe.GetAsync(cancellationToken));
        Assert.Equal(2, probe.Handshakes);
    }

    public void Dispose() => _folder.Dispose();

    private async Task<ServerCertificates> CertificatesAsync()
    {
        ServerCertificates certificates = new(_folder.Files, s_names, generate: true, _clock);
        await certificates.CheckAsync(TestContext.Current.CancellationToken);

        return certificates;
    }

    // The endpoint answers ok only when the request knows which pair its connection was served.
    private async Task<WebApplication> StartAsync(ServerCertificates certificates)
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
        builder.AddDdtServerCertificates(certificates);

        WebApplication app = builder.Build();
        app.MapGet("/", (HttpContext context) => ServerCertificateExtensions.ServedThumbprint(context) is null ? "unknown" : "ok");
        await app.StartAsync(TestContext.Current.CancellationToken);

        return app;
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}

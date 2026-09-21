// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DDT.Server.Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ServerCertificateTests : IDisposable
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";

    private static readonly TimeSpan s_second = TimeSpan.FromSeconds(1);

    private readonly CertificateFolder _folder = new();
    private readonly ManualTimeProvider _clock = new();

    [Fact]
    public async Task TheRootIsACertificateAuthorityThatIssuesNothingBelowTheServerCertificate()
    {
        DateTimeOffset start = _clock.GetUtcNow();

        Assert.Equal(CertificateAction.Created, (await CheckAsync(Certificates("ddt.example"))).Action);

        using X509Certificate2 root = _folder.Root();
        X509BasicConstraintsExtension constraints = root.Extensions.OfType<X509BasicConstraintsExtension>().Single();
        Assert.True(constraints.CertificateAuthority);
        Assert.True(constraints.HasPathLengthConstraint);
        Assert.Equal(0, constraints.PathLengthConstraint);
        Assert.Equal(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
            root.Extensions.OfType<X509KeyUsageExtension>().Single().KeyUsages);
        Assert.StartsWith("CN=DDT root ", root.Subject, StringComparison.Ordinal);
        Assert.Equal(root.Subject, root.Issuer);
        Assert.Equal(start.AddYears(20), new DateTimeOffset(root.NotAfter), s_second);
    }

    [Fact]
    public async Task TheServerCertificateChainsToTheRootUnderTheAgentsPolicy()
    {
        DateTimeOffset start = _clock.GetUtcNow();
        ServerCertificates certificates = Certificates("ddt.example, 10.10.0.5");
        CertificateCheck check = await CheckAsync(certificates);

        using X509Certificate2 root = _folder.Root();
        using X509Certificate2 certificate = _folder.Certificate();

        Assert.True(check.ManagedByDdt);
        Assert.Equal(certificate.Thumbprint, certificates.Current?.Thumbprint);
        Assert.True(certificates.Current?.HasPrivateKey);
        Assert.True(CertificateFolder.ChainsUnderTheAgentsPolicy(certificate, root, start));
        Assert.Equal(root.Subject, certificate.Issuer);
        Assert.False(certificate.Extensions.OfType<X509BasicConstraintsExtension>().Single().CertificateAuthority);
        Assert.Contains(
            certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single().EnhancedKeyUsages.Cast<Oid>(),
            oid => oid.Value == ServerAuthentication);
        Assert.Equal(start.AddDays(90), new DateTimeOffset(certificate.NotAfter), s_second);

        IReadOnlyList<string> names = ServerNames.Of(certificate);
        Assert.Contains("localhost", names);
        Assert.Contains(Dns.GetHostName(), names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ddt.example", names);
        Assert.Contains("10.10.0.5", names);
    }

    // Windows PE reads a firmware clock that holds local time as Pacific time, so machines west of that run behind.
    [Fact]
    public async Task AMachineWhoseClockRunsHoursBehindStillAcceptsANewCertificate()
    {
        DateTimeOffset start = _clock.GetUtcNow();
        await CheckAsync(Certificates("ddt.example"));

        using X509Certificate2 root = _folder.Root();
        using X509Certificate2 certificate = _folder.Certificate();

        Assert.True(CertificateFolder.ChainsUnderTheAgentsPolicy(certificate, root, start.AddHours(-12)));
    }

    [Fact]
    public async Task ACertificateFromAnotherRootIsRefusedUnderTheAgentsPolicy()
    {
        await CheckAsync(Certificates("ddt.example"));
        using CertificateFolder other = new();
        await new ServerCertificates(other.Files, ServerNames.Required("ddt.example"), true, _clock)
            .CheckAsync(TestContext.Current.CancellationToken);

        using X509Certificate2 otherRoot = other.Root();
        using X509Certificate2 certificate = _folder.Certificate();

        Assert.False(CertificateFolder.ChainsUnderTheAgentsPolicy(certificate, otherRoot, _clock.GetUtcNow()));
    }

    [Fact]
    public async Task AnExistingPairIsLoadedAsItIs()
    {
        await CheckAsync(Certificates("ddt.example"));
        string certificate = File.ReadAllText(_folder.Files.CertificatePath);

        ServerCertificates restarted = Certificates("ddt.example");
        CertificateCheck check = await CheckAsync(restarted);

        Assert.Equal(CertificateAction.Loaded, check.Action);
        Assert.True(check.ManagedByDdt);
        Assert.Equal(certificate, File.ReadAllText(_folder.Files.CertificatePath));
        Assert.Equal(CertificateAction.Unchanged, (await CheckAsync(restarted)).Action);
    }

    // Boot images pin the root, so a missing pair is issued again from the root that is there.
    [Fact]
    public async Task AMissingPairIsIssuedFromTheSameRoot()
    {
        ServerCertificates certificates = Certificates("ddt.example");
        await CheckAsync(certificates);
        string root = File.ReadAllText(_folder.Files.RootPath);
        File.Delete(_folder.Files.CertificatePath);
        File.Delete(_folder.Files.KeyPath);

        Assert.Equal(CertificateAction.Issued, (await CheckAsync(certificates)).Action);

        Assert.Equal(root, File.ReadAllText(_folder.Files.RootPath));
        using X509Certificate2 rootCertificate = _folder.Root();
        using X509Certificate2 certificate = _folder.Certificate();
        Assert.True(CertificateFolder.ChainsUnderTheAgentsPolicy(certificate, rootCertificate, _clock.GetUtcNow()));
    }

    [Fact]
    public async Task APairOfDdtsOwnThatNoLongerLoadsIsIssuedAgain()
    {
        await CheckAsync(Certificates("ddt.example"));
        File.WriteAllText(_folder.Files.KeyPath, "not a key");

        Assert.Equal(CertificateAction.Issued, (await CheckAsync(Certificates("ddt.example"))).Action);
        using X509Certificate2 certificate = _folder.Certificate();
    }

    [Fact]
    public async Task OnlyTheOwnerCanReadTheKeys()
    {
        await CheckAsync(Certificates("ddt.example"));

        foreach (string key in new[] { _folder.Files.KeyPath, _folder.Files.RootKeyPath })
        {
            Assert.Contains("PRIVATE KEY", File.ReadAllText(key), StringComparison.Ordinal);

            // Windows has no file mode; the store's folder permissions apply there.
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(key));
            }
        }

        Assert.DoesNotContain("PRIVATE KEY", File.ReadAllText(_folder.Files.RootPath), StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", File.ReadAllText(_folder.Files.CertificatePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingIsRenewedWithMoreThanThirtyDaysLeft()
    {
        ServerCertificates certificates = Certificates("ddt.example");
        await CheckAsync(certificates);
        string certificate = File.ReadAllText(_folder.Files.CertificatePath);

        _clock.Advance(TimeSpan.FromDays(59));

        Assert.Equal(CertificateAction.Unchanged, (await CheckAsync(certificates)).Action);
        Assert.Equal(certificate, File.ReadAllText(_folder.Files.CertificatePath));
        Assert.False(File.Exists(_folder.Files.PreviousCertificatePath));
    }

    [Fact]
    public async Task TheCertificateIsRenewedFromTheSameRootWithThirtyDaysLeft()
    {
        ServerCertificates certificates = Certificates("ddt.example");
        await CheckAsync(certificates);
        string root = File.ReadAllText(_folder.Files.RootPath);
        string oldCertificate = File.ReadAllText(_folder.Files.CertificatePath);
        string oldKey = File.ReadAllText(_folder.Files.KeyPath);

        _clock.Advance(TimeSpan.FromDays(61));
        CertificateCheck check = await CheckAsync(certificates);

        Assert.Equal(CertificateAction.Renewed, check.Action);
        Assert.Equal(root, File.ReadAllText(_folder.Files.RootPath));
        using X509Certificate2 rootCertificate = _folder.Root();
        using X509Certificate2 renewed = _folder.Certificate();
        Assert.True(CertificateFolder.ChainsUnderTheAgentsPolicy(renewed, rootCertificate, _clock.GetUtcNow()));
        Assert.Equal(_clock.GetUtcNow().AddDays(90), new DateTimeOffset(renewed.NotAfter), s_second);
        Assert.Equal(renewed.Thumbprint, certificates.Current?.Thumbprint);
        Assert.Contains("ddt.example", ServerNames.Of(renewed));

        // The pair it replaced stays next to it, to go back to by hand.
        Assert.Equal(oldCertificate, File.ReadAllText(_folder.Files.PreviousCertificatePath));
        Assert.Equal(oldKey, File.ReadAllText(_folder.Files.PreviousKeyPath));
    }

    [Fact]
    public async Task AConfiguredNameMissingFromTheCertificateHasItIssuedAgain()
    {
        await CheckAsync(Certificates("ddt.example"));

        CertificateCheck check = await CheckAsync(Certificates("ddt.example, ddt.lab.example"));

        Assert.Equal(CertificateAction.Reissued, check.Action);
        using X509Certificate2 certificate = _folder.Certificate();
        Assert.Contains("ddt.lab.example", ServerNames.Of(certificate));
        Assert.Contains("ddt.example", ServerNames.Of(certificate));
    }

    // Boot images still name the server the old way, so a renewal keeps every name the certificate had.
    [Fact]
    public async Task ANameNoLongerConfiguredStaysInTheCertificate()
    {
        await CheckAsync(Certificates("ddt.example, old.example"));
        ServerCertificates certificates = Certificates("ddt.example");
        await CheckAsync(certificates);

        _clock.Advance(TimeSpan.FromDays(61));
        await CheckAsync(certificates);

        using X509Certificate2 certificate = _folder.Certificate();
        Assert.Contains("old.example", ServerNames.Of(certificate));
    }

    [Fact]
    public async Task AnAdministratorsCertificateIsServedAsItIs()
    {
        PemPair own = AdministratorCertificate.Create("CN=ddt.example", "ddt.example", _clock.GetUtcNow().AddDays(-1), _clock.GetUtcNow().AddDays(365));
        AdministratorCertificate.Write(_folder.Files, own);
        ServerCertificates certificates = Certificates("ddt.example");

        CertificateCheck check = await CheckAsync(certificates);

        Assert.Equal(CertificateAction.Loaded, check.Action);
        Assert.False(check.ManagedByDdt);
        Assert.False(File.Exists(_folder.Files.RootPath));
        Assert.Equal(own.CertificatePem, File.ReadAllText(_folder.Files.CertificatePath));
        Assert.Equal(own.KeyPem, File.ReadAllText(_folder.Files.KeyPath));
        Assert.True(certificates.Current?.HasPrivateKey);
    }

    [Fact]
    public async Task AnAdministratorsCertificateIsNeverRenewedButWarnsOnceBeforeItExpires()
    {
        PemPair own = AdministratorCertificate.Create("CN=ddt.example", "ddt.example", _clock.GetUtcNow().AddDays(-1), _clock.GetUtcNow().AddDays(40));
        AdministratorCertificate.Write(_folder.Files, own);
        ServerCertificates certificates = Certificates("ddt.example");
        Assert.False((await CheckAsync(certificates)).ExpiresSoon);

        _clock.Advance(TimeSpan.FromDays(11));
        CertificateCheck warned = await CheckAsync(certificates);
        CertificateCheck later = await CheckAsync(certificates);

        Assert.Equal(CertificateAction.Unchanged, warned.Action);
        Assert.True(warned.ExpiresSoon);
        Assert.False(later.ExpiresSoon);
        Assert.Equal(own.CertificatePem, File.ReadAllText(_folder.Files.CertificatePath));
    }

    [Fact]
    public async Task AnAdministratorsReplacedCertificateIsLoadedAgain()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        AdministratorCertificate.Write(_folder.Files, AdministratorCertificate.Create("CN=first.example", "first.example", now.AddDays(-1), now.AddDays(365)));
        ServerCertificates certificates = Certificates(string.Empty);
        await CheckAsync(certificates);

        AdministratorCertificate.Write(_folder.Files, AdministratorCertificate.Create("CN=second.example", "second.example", now.AddDays(-1), now.AddDays(365)));
        CertificateCheck check = await CheckAsync(certificates);

        Assert.Equal(CertificateAction.Reloaded, check.Action);
        Assert.Equal("CN=second.example", certificates.Current?.Subject);
    }

    [Fact]
    public async Task ChangedFilesThatDoNotLoadLeaveTheCertificateBeforeInService()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        PemPair first = AdministratorCertificate.Create("CN=first.example", "first.example", now.AddDays(-1), now.AddDays(365));
        PemPair second = AdministratorCertificate.Create("CN=second.example", "second.example", now.AddDays(-1), now.AddDays(365));
        AdministratorCertificate.Write(_folder.Files, first);
        ServerCertificates certificates = Certificates(string.Empty);
        await CheckAsync(certificates);

        // Half way through replacing the pair: the new certificate, the old key.
        File.WriteAllText(_folder.Files.CertificatePath, second.CertificatePem);
        CertificateCheck check = await CheckAsync(certificates);

        Assert.Equal(CertificateAction.LoadFailed, check.Action);
        Assert.NotNull(check.Problem);
        Assert.Equal("CN=first.example", certificates.Current?.Subject);
        Assert.Equal(CertificateAction.Unchanged, (await CheckAsync(certificates)).Action);

        File.WriteAllText(_folder.Files.KeyPath, second.KeyPem);
        Assert.Equal(CertificateAction.Reloaded, (await CheckAsync(certificates)).Action);
        Assert.Equal("CN=second.example", certificates.Current?.Subject);
    }

    [Fact]
    public async Task WithoutGenerationNothingIsWritten()
    {
        ServerCertificates certificates = new(_folder.Files, ServerNames.Required("ddt.example"), false, _clock);

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => CheckAsync(certificates));

        Assert.Contains(_folder.Files.CertificatePath, refusal.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(_folder.Files.RootPath));
        Assert.False(File.Exists(_folder.Files.CertificatePath));
    }

    [Fact]
    public async Task WithoutGenerationDdtsOwnCertificateIsNeverRenewed()
    {
        await CheckAsync(Certificates("ddt.example"));
        string certificate = File.ReadAllText(_folder.Files.CertificatePath);
        ServerCertificates certificates = new(_folder.Files, ServerNames.Required("ddt.example"), false, _clock);

        _clock.Advance(TimeSpan.FromDays(61));
        CertificateCheck check = await CheckAsync(certificates);

        Assert.False(check.ManagedByDdt);
        Assert.True(check.ExpiresSoon);
        Assert.Equal(certificate, File.ReadAllText(_folder.Files.CertificatePath));
    }

    // A new root would break every boot image, so a root that lost its key is never replaced on its own.
    [Fact]
    public async Task ARootWithoutItsKeyStopsTheCheck()
    {
        await CheckAsync(Certificates("ddt.example"));
        string root = File.ReadAllText(_folder.Files.RootPath);
        File.Delete(_folder.Files.RootKeyPath);

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CheckAsync(Certificates("ddt.example")));

        Assert.Contains(_folder.Files.RootKeyPath, refusal.Message, StringComparison.Ordinal);
        Assert.Equal(root, File.ReadAllText(_folder.Files.RootPath));
    }

    [Fact]
    public async Task TheRenewerChecksEveryFiveMinutes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ServerCertificates certificates = Certificates("ddt.example");
        await CheckAsync(certificates);
        X509Certificate2 first = certificates.Current!;

        using ServerCertificateRenewer renewer = new(certificates, _clock, NullLogger<ServerCertificateRenewer>.Instance);
        await renewer.StartAsync(cancellationToken);

        while (!_clock.HasTimerDueIn(ServerCertificateRenewer.Interval))
        {
            await Task.Delay(10, cancellationToken);
        }

        _clock.Advance(TimeSpan.FromDays(61));

        while (ReferenceEquals(certificates.Current, first))
        {
            await Task.Delay(10, cancellationToken);
        }

        await renewer.StopAsync(cancellationToken);

        using X509Certificate2 renewed = _folder.Certificate();
        Assert.Equal(renewed.Thumbprint, certificates.Current?.Thumbprint);
        Assert.NotEqual(first.Thumbprint, renewed.Thumbprint);
    }

    public void Dispose() => _folder.Dispose();

    private ServerCertificates Certificates(string configuredNames) =>
        new(_folder.Files, ServerNames.Required(configuredNames), true, _clock);

    private static Task<CertificateCheck> CheckAsync(ServerCertificates certificates) =>
        certificates.CheckAsync(TestContext.Current.CancellationToken);
}

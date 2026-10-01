// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using DDT.Contracts.Server;
using DDT.Server.Certificates;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ServerCertificateTests : IDisposable
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";

    private static readonly TimeSpan s_second = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_renewerTimeout = TimeSpan.FromSeconds(10);

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

    // Windows PE reads a firmware clock that holds local time as if it were Pacific time.
    // So machines west of Pacific time run behind.
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
        Assert.Equal("not a key", File.ReadAllText(_folder.Files.PreviousKeyPath));
    }

    // Halfway through moving a store that DDT managed to your own certificate, the new certificate sits next to DDT's
    // old key. Nothing is issued over it, neither while DDT runs nor at the next start.
    [Fact]
    public async Task AnAdministratorsCertificateNextToDdtsOldKeyIsLeftAlone()
    {
        ServerCertificates certificates = Certificates("ddt.example");
        await CheckAsync(certificates);
        string served = certificates.Current!.Thumbprint;
        string key = File.ReadAllText(_folder.Files.KeyPath);
        DateTimeOffset now = _clock.GetUtcNow();
        PemPair own = AdministratorCertificate.Create("CN=ddt.example", "ddt.example", now.AddDays(-1), now.AddDays(365));
        File.WriteAllText(_folder.Files.CertificatePath, own.CertificatePem);

        CertificateCheck check = await CheckAsync(certificates);

        Assert.Equal(CertificateAction.LoadFailed, check.Action);
        Assert.Equal(served, certificates.Current?.Thumbprint);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CheckAsync(Certificates("ddt.example")));
        Assert.Equal(own.CertificatePem, File.ReadAllText(_folder.Files.CertificatePath));
        Assert.Equal(key, File.ReadAllText(_folder.Files.KeyPath));
        Assert.False(File.Exists(_folder.Files.PreviousCertificatePath));
    }

    [Fact]
    public async Task OnlyTheOwnerCanReadTheKeys()
    {
        await CheckAsync(Certificates("ddt.example"));

        foreach (string key in new[] { _folder.Files.KeyPath, _folder.Files.RootKeyPath })
        {
            Assert.Contains("PRIVATE KEY", File.ReadAllText(key), StringComparison.Ordinal);

            if (OperatingSystem.IsWindows())
            {
                // Nothing inherited from the folder, and only the account, SYSTEM and administrators in the list.
                using WindowsIdentity account = WindowsIdentity.GetCurrent();
                SecurityIdentifier[] allowed =
                [
                    account.User!,
                    new(WellKnownSidType.LocalSystemSid, null),
                    new(WellKnownSidType.BuiltinAdministratorsSid, null),
                ];
                FileSecurity security = new FileInfo(key).GetAccessControl();

                Assert.True(security.AreAccessRulesProtected);

                foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
                {
                    Assert.Contains((SecurityIdentifier)rule.IdentityReference, allowed);
                }
            }
            else
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

    // A store whose server was off for months. The certificate expired by the machine's clock, but it came from the
    // root. So it's renewed instead of served as someone else's.
    [Fact]
    public async Task AnExpiredCertificateFromTheRootIsRenewed()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PemPair root = ServerCertificateAuthority.CreateRoot(now.AddDays(-200));
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(_folder.Files.RootKeyPath, root.KeyPem);
        File.WriteAllText(_folder.Files.RootPath, root.CertificatePem);
        CertificateFolder.Write(_folder.Files, ServerCertificateAuthority.Issue(root, ServerNames.Required("ddt.example"), [], now.AddDays(-120)));

        CertificateCheck check = await CheckAsync(Certificates("ddt.example"));

        Assert.Equal(CertificateAction.Renewed, check.Action);
        Assert.True(check.ManagedByDdt);
        Assert.Equal(root.CertificatePem, File.ReadAllText(_folder.Files.RootPath));
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
        CertificateFolder.Write(_folder.Files, own);
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
        CertificateFolder.Write(_folder.Files, own);
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
        CertificateFolder.Write(_folder.Files, AdministratorCertificate.Create("CN=first.example", "first.example", now.AddDays(-1), now.AddDays(365)));
        ServerCertificates certificates = Certificates(string.Empty);
        await CheckAsync(certificates);

        CertificateFolder.Write(_folder.Files, AdministratorCertificate.Create("CN=second.example", "second.example", now.AddDays(-1), now.AddDays(365)));
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
        CertificateFolder.Write(_folder.Files, first);
        ServerCertificates certificates = Certificates(string.Empty);
        await CheckAsync(certificates);

        // Halfway through replacing the pair, with the new certificate and the old key.
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
        Assert.False(Directory.Exists(_folder.Path));
    }

    // A folder DDT can't write to. For example a read-only mount, where compose puts anything outside the store volume,
    // or a folder another user owns.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAdministratorsCertificateInAFolderDdtCannotWriteToIsServed(bool generate)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        PemPair own = AdministratorCertificate.Create("CN=ddt.example", "ddt.example", now.AddDays(-1), now.AddDays(365));
        CertificateFolder.Write(_folder.Files, own);
        _folder.DenyWrites();

        CertificateCheck check = await CheckAsync(new ServerCertificates(_folder.Files, ServerNames.Required("ddt.example"), generate, _clock));

        Assert.Equal(CertificateAction.Loaded, check.Action);
        Assert.False(check.ManagedByDdt);
        Assert.False(File.Exists(_folder.Files.LockPath));
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

    // An administrator's dotnet run made the key, and the service's account then finds it closed.
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task ARootKeyDdtMayNotReadNamesTheAccountDdtRunsAs()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Root reads every file on Linux, so only a Windows ACL closes one here.");
        await CheckAsync(Certificates("ddt.example"));
        CertificateFolder.DenyReading(_folder.Files.RootKeyPath);
        using WindowsIdentity account = WindowsIdentity.GetCurrent();

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CheckAsync(Certificates("ddt.example")));

        Assert.Contains($"DDT runs as {account.Name}. Give that account read access", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("backup", refusal.Message, StringComparison.Ordinal);
    }

    // Boot images built before pin the old certificate, which isn't a CA.
    // So they need one rebuild, and everything else carries on.
    [Fact]
    public async Task TheSelfSignedCertificateFromBeforeTheRootIsReplacedByOneFromANewRoot()
    {
        PemPair legacy = LegacyCertificate.Create("localhost", "ddt.lab.example");
        CertificateFolder.Write(_folder.Files, legacy);
        ServerCertificates certificates = Certificates("ddt.example");

        CertificateCheck check = await CheckAsync(certificates);

        Assert.Equal(CertificateAction.Migrated, check.Action);
        Assert.True(check.ManagedByDdt);
        using X509Certificate2 root = _folder.Root();
        using X509Certificate2 certificate = _folder.Certificate();
        Assert.True(CertificateFolder.ChainsUnderTheAgentsPolicy(certificate, root, _clock.GetUtcNow()));
        Assert.Contains("ddt.lab.example", ServerNames.Of(certificate));
        Assert.Contains("ddt.example", ServerNames.Of(certificate));

        Assert.Equal(legacy.CertificatePem, File.ReadAllText(_folder.Files.ReplacedAnchorPath));
        Assert.Equal(legacy.CertificatePem, File.ReadAllText(_folder.Files.PreviousCertificatePath));
        Assert.Equal(legacy.KeyPem, File.ReadAllText(_folder.Files.PreviousKeyPath));

        using X509Certificate2 anchor = X509Certificate2.CreateFromPem(legacy.CertificatePem);
        Assert.Equal(anchor.GetCertHashString(HashAlgorithmName.SHA256), certificates.ReplacedAnchorSha256());
        Assert.NotNull(certificates.Describe()?.AnchorReplacedUtc);
        Assert.Equal(CertificateAction.Loaded, (await CheckAsync(Certificates("ddt.example"))).Action);
    }

    [Fact]
    public async Task WithoutGenerationTheSelfSignedCertificateFromBeforeTheRootStays()
    {
        PemPair legacy = LegacyCertificate.Create("localhost");
        CertificateFolder.Write(_folder.Files, legacy);

        CertificateCheck check = await CheckAsync(new ServerCertificates(_folder.Files, ServerNames.Required(string.Empty), false, _clock));

        Assert.Equal(CertificateAction.Loaded, check.Action);
        Assert.False(File.Exists(_folder.Files.RootPath));
        Assert.False(File.Exists(_folder.Files.ReplacedAnchorPath));
        Assert.Equal(legacy.CertificatePem, File.ReadAllText(_folder.Files.CertificatePath));
    }

    // Only what DDT generated itself is replaced. A certificate from an administrator's CA stays, whatever its name.
    [Fact]
    public async Task ACertificateNamedLikeDdtsButIssuedByAnotherCaIsNotReplaced()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        PemPair authority = AdministratorCertificate.CreateAuthority("CN=Example CA", null, now.AddDays(-2), now.AddYears(5));
        PemPair own = AdministratorCertificate.Issue(authority, "CN=DDT", ["ddt.example"], now.AddDays(-1), now.AddDays(365));
        CertificateFolder.Write(_folder.Files, own);

        CertificateCheck check = await CheckAsync(Certificates("ddt.example"));

        Assert.Equal(CertificateAction.Loaded, check.Action);
        Assert.False(check.ManagedByDdt);
        Assert.False(File.Exists(_folder.Files.RootPath));
        Assert.False(File.Exists(_folder.Files.ReplacedAnchorPath));
        Assert.Equal(own.CertificatePem, File.ReadAllText(_folder.Files.CertificatePath));
        Assert.Equal(own.KeyPem, File.ReadAllText(_folder.Files.KeyPath));
    }

    [Fact]
    public async Task TheViewDescribesTheCertificateAndTheRootItComesFrom()
    {
        ServerCertificates certificates = Certificates("ddt.example");
        await CheckAsync(certificates);

        ServerCertificateView view = Assert.IsType<ServerCertificateView>(certificates.Describe());

        using X509Certificate2 root = _folder.Root();
        using X509Certificate2 certificate = _folder.Certificate();
        Assert.True(view.ManagedByDdt);
        Assert.Equal(certificate.GetCertHashString(HashAlgorithmName.SHA256), view.Sha256);
        Assert.Equal(new DateTimeOffset(certificate.NotAfter.ToUniversalTime()), view.NotAfter);
        Assert.Equal(view.NotAfter.AddDays(-30), view.RenewsUtc);
        Assert.Contains("ddt.example", view.Names);
        Assert.Equal(root.Subject, view.RootSubject);
        Assert.Equal(root.GetCertHashString(HashAlgorithmName.SHA256), view.RootSha256);
        Assert.Null(view.AnchorReplacedUtc);
        Assert.Equal(File.ReadAllText(_folder.Files.RootPath), certificates.RootCertificatePem);
    }

    [Fact]
    public async Task AnAdministratorsCertificateHasNoRootToOffer()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        CertificateFolder.Write(_folder.Files, AdministratorCertificate.Create("CN=ddt.example", "ddt.example", now.AddDays(-1), now.AddDays(365)));
        ServerCertificates certificates = Certificates("ddt.example");
        await CheckAsync(certificates);

        ServerCertificateView view = Assert.IsType<ServerCertificateView>(certificates.Describe());

        Assert.False(view.ManagedByDdt);
        Assert.Null(view.RenewsUtc);
        Assert.Null(view.RootSha256);
        Assert.Null(certificates.RootCertificatePem);
    }

    // The renewer checks again and again.
    // The first check picks up a pair another process wrote, and a later one renews.
    [Fact]
    public async Task TheRenewerChecksEveryFiveMinutes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ServerCertificates certificates = Certificates("ddt.example");
        await CheckAsync(certificates);
        X509Certificate2 first = certificates.Current!;

        using RecordingLoggerProvider log = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(log));
        using ServerCertificateRenewer renewer = new(certificates, _clock, loggerFactory.CreateLogger<ServerCertificateRenewer>());
        await renewer.StartAsync(cancellationToken);
        await UntilAsync(() => _clock.HasTimerDueIn(TimeSpan.FromMinutes(5)), "The renewer set no five minute timer.", cancellationToken);

        PemPair root = new(File.ReadAllText(_folder.Files.RootPath), File.ReadAllText(_folder.Files.RootKeyPath));
        CertificateFolder.Write(_folder.Files, ServerCertificateAuthority.Issue(root, certificates.Names, [], _clock.GetUtcNow()));
        _clock.Advance(TimeSpan.FromMinutes(5));
        await UntilAsync(() => !ReferenceEquals(certificates.Current, first), "The first check did not load the new pair.", cancellationToken);
        X509Certificate2 reloaded = certificates.Current!;

        _clock.Advance(TimeSpan.FromDays(61));
        await UntilAsync(() => !ReferenceEquals(certificates.Current, reloaded), "No later check renewed the certificate.", cancellationToken);
        await renewer.StopAsync(cancellationToken);

        using X509Certificate2 renewed = _folder.Certificate();
        Assert.Equal(renewed.Thumbprint, certificates.Current?.Thumbprint);
        Assert.Equal(_clock.GetUtcNow().AddDays(90), new DateTimeOffset(renewed.NotAfter), s_second);
        Assert.Equal([854, 851], log.Entries.Select(entry => entry.EventId.Id));
    }

    // Before anyone trusts the root, they compare it with what DDT logged when it made it.
    [Fact]
    public async Task MakingTheRootLogsWhereItIsAndItsSha256()
    {
        using RecordingLoggerProvider log = new();
        ServerCertificates certificates = Certificates("ddt.example");

        CertificateLog.Checked(log.CreateLogger("DDT"), certificates, await CheckAsync(certificates));

        LogEntry created = Assert.Single(log.Entries);
        Assert.Equal(850, created.EventId.Id);
        Assert.Equal(LogLevel.Warning, created.Level);
        Assert.Contains(_folder.Files.RootPath, created.Message, StringComparison.Ordinal);
        Assert.Contains(certificates.Describe()!.RootSha256!, created.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAdministratorsCertificateThatExpiresSoonIsLoggedOnce()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        CertificateFolder.Write(_folder.Files, AdministratorCertificate.Create("CN=ddt.example", "ddt.example", now.AddDays(-1), now.AddDays(40)));
        using RecordingLoggerProvider log = new();
        ILogger logger = log.CreateLogger("DDT");
        ServerCertificates certificates = Certificates("ddt.example");
        CertificateLog.Checked(logger, certificates, await CheckAsync(certificates));

        _clock.Advance(TimeSpan.FromDays(11));
        CertificateLog.Checked(logger, certificates, await CheckAsync(certificates));
        CertificateLog.Checked(logger, certificates, await CheckAsync(certificates));

        LogEntry warning = Assert.Single(log.Entries);
        Assert.Equal(855, warning.EventId.Id);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains(_folder.Files.CertificatePath, warning.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _folder.Dispose();

    private ServerCertificates Certificates(string configuredNames) =>
        new(_folder.Files, ServerNames.Required(configuredNames), true, _clock);

    private static Task<CertificateCheck> CheckAsync(ServerCertificates certificates) =>
        certificates.CheckAsync(TestContext.Current.CancellationToken);

    // The renewer checks on its own thread, so a check that never comes fails the test instead of hanging it.
    private static async Task UntilAsync(Func<bool> condition, string failure, CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();

        while (!condition())
        {
            Assert.True(Stopwatch.GetElapsedTime(start) < s_renewerTimeout, failure);
            await Task.Delay(10, cancellationToken);
        }
    }
}

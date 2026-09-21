// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography.X509Certificates;
using DDT.Server.Certificates;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ServerCertificateTests : IDisposable
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";

    private static readonly DateTimeOffset s_start = new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    private readonly CertificateFolder _folder = new();

    [Fact]
    public void TheRootIsACertificateAuthorityThatIssuesNothingBelowTheServerCertificate()
    {
        ServerCertificates certificates = Certificates("ddt.example");

        Assert.Equal(CertificateAction.Created, certificates.EnsureExists(s_start));

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
        Assert.Equal(s_start.AddYears(20), new DateTimeOffset(root.NotAfter));
    }

    [Fact]
    public void TheServerCertificateChainsToTheRootUnderTheAgentsPolicy()
    {
        ServerCertificates certificates = Certificates("ddt.example, 10.10.0.5");
        certificates.EnsureExists(s_start);

        using X509Certificate2 root = _folder.Root();
        using X509Certificate2 certificate = _folder.Certificate();

        Assert.True(CertificateFolder.ChainsUnderTheAgentsPolicy(certificate, root, s_start));
        Assert.Equal(root.Subject, certificate.Issuer);
        Assert.True(certificate.HasPrivateKey);
        Assert.False(certificate.Extensions.OfType<X509BasicConstraintsExtension>().Single().CertificateAuthority);
        Assert.Contains(
            certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single().EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>(),
            oid => oid.Value == ServerAuthentication);
        Assert.Equal(s_start.AddDays(90), new DateTimeOffset(certificate.NotAfter));

        IReadOnlyList<string> names = ServerNames.Of(certificate);
        Assert.Contains("localhost", names);
        Assert.Contains(Dns.GetHostName(), names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ddt.example", names);
        Assert.Contains("10.10.0.5", names);
    }

    // Windows PE reads a firmware clock that holds local time as Pacific time, so machines west of that run behind.
    [Fact]
    public void AMachineWhoseClockRunsHoursBehindStillAcceptsANewCertificate()
    {
        Certificates("ddt.example").EnsureExists(s_start);

        using X509Certificate2 root = _folder.Root();
        using X509Certificate2 certificate = _folder.Certificate();

        Assert.True(CertificateFolder.ChainsUnderTheAgentsPolicy(certificate, root, s_start.AddHours(-12)));
    }

    [Fact]
    public void ACertificateFromAnotherRootIsRefusedUnderTheAgentsPolicy()
    {
        Certificates("ddt.example").EnsureExists(s_start);
        using CertificateFolder other = new();
        new ServerCertificates(other.Files, ServerNames.Required("ddt.example")).EnsureExists(s_start);

        using X509Certificate2 otherRoot = other.Root();
        using X509Certificate2 certificate = _folder.Certificate();

        Assert.False(CertificateFolder.ChainsUnderTheAgentsPolicy(certificate, otherRoot, s_start));
    }

    [Fact]
    public void AnExistingPairIsLeftAsItIs()
    {
        ServerCertificates certificates = Certificates("ddt.example");
        certificates.EnsureExists(s_start);
        string certificate = File.ReadAllText(_folder.Files.CertificatePath);

        Assert.Equal(CertificateAction.Unchanged, certificates.EnsureExists(s_start.AddDays(1)));
        Assert.Equal(certificate, File.ReadAllText(_folder.Files.CertificatePath));
    }

    // Boot images pin the root, so a missing pair is issued again from the root that is there.
    [Fact]
    public void AMissingPairIsIssuedFromTheSameRoot()
    {
        ServerCertificates certificates = Certificates("ddt.example");
        certificates.EnsureExists(s_start);
        string root = File.ReadAllText(_folder.Files.RootPath);
        File.Delete(_folder.Files.CertificatePath);
        File.Delete(_folder.Files.KeyPath);

        Assert.Equal(CertificateAction.Issued, certificates.EnsureExists(s_start.AddDays(1)));

        Assert.Equal(root, File.ReadAllText(_folder.Files.RootPath));
        using X509Certificate2 rootCertificate = _folder.Root();
        using X509Certificate2 certificate = _folder.Certificate();
        Assert.True(CertificateFolder.ChainsUnderTheAgentsPolicy(certificate, rootCertificate, s_start.AddDays(1)));
    }

    [Fact]
    public void OnlyTheOwnerCanReadTheKeys()
    {
        Certificates("ddt.example").EnsureExists(s_start);

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

    public void Dispose() => _folder.Dispose();

    private ServerCertificates Certificates(string configuredNames) =>
        new(_folder.Files, ServerNames.Required(configuredNames));
}

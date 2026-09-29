// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Certificates;

// Boot images pin the root, so it has to outlive them. The server certificate it issues can then change as often as
// needed without a new boot image.
public static class ServerCertificateAuthority
{
    public const int RootLifetimeYears = 20;

    public static readonly TimeSpan CertificateLifetime = TimeSpan.FromDays(90);

    private const int KeySize = 3072;

    private static readonly Oid s_serverAuthentication = new("1.3.6.1.5.5.7.3.1");

    // WinPE often runs hours behind. It reads a firmware clock that holds local time as if it were Pacific time. Such a
    // machine must not treat a certificate issued a moment ago as not yet valid.
    private static readonly TimeSpan s_backdate = TimeSpan.FromDays(1);

    // Creates a CA that can issue server certificates but nothing below them.
    public static PemPair CreateRoot(DateTimeOffset now)
    {
        using RSA key = RSA.Create(KeySize);
        string id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        CertificateRequest request = new($"CN=DDT root {id}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using X509Certificate2 root = request.CreateSelfSigned(now - s_backdate, now.AddYears(RootLifetimeYears));

        return new PemPair(root.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem());
    }

    public static PemPair Issue(PemPair root, IEnumerable<string> names, IEnumerable<IPAddress> addresses, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(addresses);

        using X509Certificate2 issuer = X509Certificate2.CreateFromPem(root.CertificatePem);
        using RSA issuerKey = RSA.Create();
        issuerKey.ImportFromPem(root.KeyPem);

        using RSA key = RSA.Create(KeySize);
        CertificateRequest request = new("CN=DDT", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([s_serverAuthentication], true));
        request.CertificateExtensions.Add(SubjectAlternativeNames(names, addresses));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, true, false));

        // Clamped to the root, which must cover the whole validity of what it issues.
        DateTimeOffset notBefore = Later(now - s_backdate, new DateTimeOffset(issuer.NotBefore));
        DateTimeOffset notAfter = Earlier(now + CertificateLifetime, new DateTimeOffset(issuer.NotAfter));

        using X509Certificate2 certificate = request.Create(
            issuer.SubjectName,
            X509SignatureGenerator.CreateForRSA(issuerKey, RSASignaturePadding.Pkcs1),
            notBefore,
            notAfter,
            SerialNumber());

        return new PemPair(certificate.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem());
    }

    private static X509Extension SubjectAlternativeNames(IEnumerable<string> names, IEnumerable<IPAddress> addresses)
    {
        SubjectAlternativeNameBuilder builder = new();
        HashSet<string> dnsNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<IPAddress> ipAddresses = [];

        foreach (string name in names)
        {
            if (IPAddress.TryParse(name, out IPAddress? address))
            {
                ipAddresses.Add(address);
            }
            else
            {
                dnsNames.Add(name);
            }
        }

        foreach (IPAddress address in addresses)
        {
            ipAddresses.Add(address);
        }

        foreach (string name in dnsNames)
        {
            builder.AddDnsName(name);
        }

        foreach (IPAddress address in ipAddresses)
        {
            builder.AddIpAddress(address);
        }

        return builder.Build();
    }

    // Random and positive, with a nonzero first byte so the encoding stays minimal.
    private static byte[] SerialNumber()
    {
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        serial[0] = (byte)((serial[0] & 0x7F) | 0x40);

        return serial;
    }

    private static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static DateTimeOffset Earlier(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}

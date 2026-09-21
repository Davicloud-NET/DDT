// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DDT.Agent.Tests;

// Certificates shaped like the server's: a root that is a CA, server certificates issued from it, and the self-signed
// certificate the server generated before it had a root.
internal static class TestCertificates
{
    private static readonly Oid s_serverAuthentication = new("1.3.6.1.5.5.7.3.1");

    public static X509Certificate2 Root(string name)
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        return Persisted(request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1)));
    }

    public static X509Certificate2 Issue(X509Certificate2 root, string address)
    {
        ArgumentNullException.ThrowIfNull(root);

        using RSA key = RSA.Create(2048);
        CertificateRequest request = ServerRequest("CN=DDT", key, address);
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(root, true, false));

        byte[] serialNumber = RandomNumberGenerator.GetBytes(16);
        serialNumber[0] = (byte)((serialNumber[0] & 0x7F) | 0x40);

        using X509Certificate2 issued = request.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90), serialNumber);

        using X509Certificate2 withKey = issued.CopyWithPrivateKey(key);

        return Persisted(withKey);
    }

    // What the server generated before it had a root, and what boot images built then pin as their root.
    public static X509Certificate2 SelfSigned(string address)
    {
        using RSA key = RSA.Create(2048);

        return Persisted(ServerRequest("CN=DDT", key, address).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2)));
    }

    // The public part alone, as agent.json carries it.
    public static X509Certificate2 Pin(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return X509Certificate2.CreateFromPem(certificate.ExportCertificatePem());
    }

    private static CertificateRequest ServerRequest(string subject, RSA key, string address)
    {
        CertificateRequest request = new(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        SubjectAlternativeNameBuilder names = new();

        if (IPAddress.TryParse(address, out IPAddress? ip))
        {
            names.AddIpAddress(ip);
        }
        else
        {
            names.AddDnsName(address);
        }

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([s_serverAuthentication], true));
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        return request;
    }

    // SChannel serves only a key it can find outside the process, so the key goes through PKCS#12.
    private static X509Certificate2 Persisted(X509Certificate2 certificate)
    {
        using (certificate)
        {
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
        }
    }
}

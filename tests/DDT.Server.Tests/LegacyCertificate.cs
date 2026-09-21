// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DDT.Server.Certificates;

namespace DDT.Server.Tests;

// The self-signed certificate DDT generated before it had a root, as that version wrote it.
internal static class LegacyCertificate
{
    public static PemPair Create(params string[] dnsNames)
    {
        using RSA key = RSA.Create(3072);
        CertificateRequest request = new("CN=DDT", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        SubjectAlternativeNameBuilder names = new();

        foreach (string name in dnsNames)
        {
            names.AddDnsName(name);
        }

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], true));
        request.CertificateExtensions.Add(names.Build());

        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(2));

        return new PemPair(certificate.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem());
    }
}

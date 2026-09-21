// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DDT.Server.Certificates;

namespace DDT.Server.Tests;

// A certificate DDT did not issue, as an administrator brings one from their own CA or makes one by hand.
internal static class AdministratorCertificate
{
    public static PemPair Create(string subject, string dnsName, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        SubjectAlternativeNameBuilder names = new();
        names.AddDnsName(dnsName);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(names.Build());

        using X509Certificate2 certificate = request.CreateSelfSigned(notBefore, notAfter);

        return new PemPair(certificate.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem());
    }

    public static void Write(CertificateFiles files, PemPair pair)
    {
        Directory.CreateDirectory(files.Folder);
        File.WriteAllText(files.CertificatePath, pair.CertificatePem);
        File.WriteAllText(files.KeyPath, pair.KeyPem);
    }
}

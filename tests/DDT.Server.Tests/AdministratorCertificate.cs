// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
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

    // The root of an administrator's CA, or with an issuer, an intermediate CA below it.
    public static PemPair CreateAuthority(string subject, PemPair? issuer, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        return Sign(request, key, issuer, notBefore, notAfter);
    }

    // A server certificate from an administrator's CA, for DNS names and addresses.
    public static PemPair Issue(PemPair issuer, string subject, IEnumerable<string> names, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(names);

        using RSA key = RSA.Create(2048);
        CertificateRequest request = new(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        SubjectAlternativeNameBuilder alternativeNames = new();

        foreach (string name in names)
        {
            if (IPAddress.TryParse(name, out IPAddress? address))
            {
                alternativeNames.AddIpAddress(address);
            }
            else
            {
                alternativeNames.AddDnsName(name);
            }
        }

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(alternativeNames.Build());

        return Sign(request, key, issuer, notBefore, notAfter);
    }

    private static PemPair Sign(CertificateRequest request, RSA key, PemPair? issuer, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        if (issuer is null)
        {
            using X509Certificate2 selfSigned = request.CreateSelfSigned(notBefore, notAfter);

            return new PemPair(selfSigned.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem());
        }

        using X509Certificate2 issuerCertificate = X509Certificate2.CreateFromPem(issuer.CertificatePem, issuer.KeyPem);
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuerCertificate, true, false));

        byte[] serialNumber = RandomNumberGenerator.GetBytes(16);
        serialNumber[0] = (byte)((serialNumber[0] & 0x7F) | 0x40);

        using X509Certificate2 certificate = request.Create(issuerCertificate, notBefore, notAfter, serialNumber);

        return new PemPair(certificate.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem());
    }
}

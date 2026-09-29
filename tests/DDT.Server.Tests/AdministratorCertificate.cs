// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DDT.Server.Certificates;

namespace DDT.Server.Tests;

// Certificates DDT didn't issue, like the ones an administrator brings from their own CA or makes by hand.
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

    // On Windows, serving a chain puts its CAs into the user's Intermediate Certification Authorities store, and with a
    // few dozen CAs of one name there Windows stops building chains for it. So the CAs made here are taken out again
    // when the tests end.
    private static readonly ConcurrentBag<string> s_authorities = [];

    static AdministratorCertificate() => AppDomain.CurrentDomain.ProcessExit += (_, _) => ForgetAuthorities();

    // Creates the root of an administrator's CA.
    // With an issuer, it creates an intermediate CA below that issuer instead.
    public static PemPair CreateAuthority(string subject, PemPair? issuer, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        PemPair authority = Sign(request, key, issuer, notBefore, notAfter);

        using X509Certificate2 certificate = X509Certificate2.CreateFromPem(authority.CertificatePem);
        s_authorities.Add(certificate.Thumbprint);

        return authority;
    }

    private static void ForgetAuthorities()
    {
        if (!OperatingSystem.IsWindows() || s_authorities.IsEmpty)
        {
            return;
        }

        try
        {
            using X509Store store = new(StoreName.CertificateAuthority, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);

            foreach (string thumbprint in s_authorities.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (X509Certificate2 certificate in store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false))
                {
                    store.Remove(certificate);
                    certificate.Dispose();
                }
            }
        }
        catch (CryptographicException)
        {
            // If the store can't be opened, the CAs stay in it. The tests already passed or failed by then.
        }
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

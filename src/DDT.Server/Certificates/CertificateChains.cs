// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Certificates;

internal static class CertificateChains
{
    // What DDT generated before it had a root: self-signed, not a CA, and named like this.
    private const string LegacySubject = "CN=DDT";

    // The certificate with its key, and the intermediates that follow it in its file, as Kestrel sends them from its
    // own PEM files.
    public static SslStreamCertificateContext? TryLoad(CertificateFiles files, out string? problem)
    {
        try
        {
            string certificatePem = File.ReadAllText(files.CertificatePath);
            X509Certificate2 certificate = Servable(X509Certificate2.CreateFromPem(certificatePem, File.ReadAllText(files.KeyPath)));
            X509Certificate2Collection chain = [];
            chain.ImportFromPem(certificatePem);
            problem = null;

            return SslStreamCertificateContext.Create(certificate, chain);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            problem = exception.Message;

            return null;
        }
    }

    public static bool Loads(string certificatePath, string keyPath)
    {
        try
        {
            using X509Certificate2 pair = X509Certificate2.CreateFromPem(File.ReadAllText(certificatePath), File.ReadAllText(keyPath));

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    // An expired certificate still came from the root, and is renewed rather than taken for someone else's.
    public static bool ChainsTo(X509Certificate2 certificate, X509Certificate2 root)
    {
        using X509Chain chain = new();
        chain.ChainPolicy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
            CustomTrustStore = { root },
            VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid,
        };

        return chain.Build(certificate);
    }

    public static bool IsLegacy(X509Certificate2 certificate) =>
        certificate.SubjectName.Name == LegacySubject
        && certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData)
        && certificate.Extensions.OfType<X509BasicConstraintsExtension>().All(constraints => !constraints.CertificateAuthority);

    // Whatever is there is kept next to it, to go back to by hand.
    public static void KeepAsPrevious(string path, string previousPath)
    {
        if (File.Exists(path))
        {
            File.Move(path, previousPath, overwrite: true);
        }
    }

    public static DateTimeOffset Utc(DateTime local) => new(local.ToUniversalTime());

    // SChannel cannot serve a key that exists only in memory, as one read from PEM does, so on Windows it goes through
    // PKCS#12, as Kestrel does with its own PEM files.
    private static X509Certificate2 Servable(X509Certificate2 certificate)
    {
        if (!OperatingSystem.IsWindows())
        {
            return certificate;
        }

        using (certificate)
        {
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
        }
    }
}

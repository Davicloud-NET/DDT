// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;

namespace DDT.Server.Certificates;

// A pair the settings page uploads, as a PEM chain with its key or a PFX with its password, read into the pair of PEM
// files DDT keeps.
internal static class CertificateUploads
{
    // Problem says what is wrong when Pair is null.
    public static (PemPair? Pair, SettingProblem? Problem) Read(CertificateUpload? upload)
    {
        if (upload is null)
        {
            return (null, new SettingProblem("certificatePem", ServerMessages.SettingsCertificateSendPair.With()));
        }

        if (!string.IsNullOrWhiteSpace(upload.Pfx))
        {
            return ReadPfx(upload.Pfx, upload.PfxPassword);
        }

        if (string.IsNullOrWhiteSpace(upload.CertificatePem) || string.IsNullOrWhiteSpace(upload.KeyPem))
        {
            return (null, new SettingProblem("certificatePem", ServerMessages.SettingsCertificateSendPem.With()));
        }

        try
        {
            using X509Certificate2 pair = X509Certificate2.CreateFromPem(upload.CertificatePem, upload.KeyPem);

            return (new PemPair(upload.CertificatePem.Trim() + "\n", upload.KeyPem.Trim() + "\n"), null);
        }
        catch (CryptographicException exception)
        {
            return (null, new SettingProblem("keyPem", ServerMessages.SettingsCertificateKeyMismatch.With("error", exception.Message)));
        }
    }

    // The certificate with the key comes first, then the rest of the chain the PFX holds.
    private static (PemPair? Pair, SettingProblem? Problem) ReadPfx(string base64, string? password)
    {
        byte[] pfx;

        try
        {
            pfx = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return (null, new SettingProblem("pfx", ServerMessages.SettingsCertificateNotBase64.With()));
        }

        try
        {
            X509Certificate2Collection collection = X509CertificateLoader.LoadPkcs12Collection(pfx, password, X509KeyStorageFlags.Exportable);

            try
            {
                return Pair(collection);
            }
            finally
            {
                foreach (X509Certificate2 certificate in collection)
                {
                    certificate.Dispose();
                }
            }
        }
        catch (CryptographicException exception)
        {
            return (null, new SettingProblem("pfx", ServerMessages.SettingsCertificatePfxPassword.With("error", exception.Message)));
        }
    }

    private static (PemPair? Pair, SettingProblem? Problem) Pair(X509Certificate2Collection collection)
    {
        if (collection.FirstOrDefault(certificate => certificate.HasPrivateKey) is not { } leaf)
        {
            return (null, new SettingProblem("pfx", ServerMessages.SettingsCertificatePfxWithoutKey.With()));
        }

        string? key = leaf.GetRSAPrivateKey()?.ExportPkcs8PrivateKeyPem() ?? leaf.GetECDsaPrivateKey()?.ExportPkcs8PrivateKeyPem();

        if (key is null)
        {
            return (null, new SettingProblem("pfx", ServerMessages.SettingsCertificateKeyAlgorithm.With()));
        }

        string chain = string.Join("\n", [leaf.ExportCertificatePem(), .. collection.Where(other => other != leaf).Select(other => other.ExportCertificatePem())]);

        return (new PemPair(chain + "\n", key + "\n"), null);
    }
}

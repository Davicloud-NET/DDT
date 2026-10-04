// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Certificates;

// DDT's own root. Boot images pin it, so a certificate issued from it can change without a new boot image.
internal sealed class CertificateRoot(CertificateFiles files)
{
    public bool Exists => File.Exists(files.RootPath);

    // Returns null while there's no root.
    public PemPair? Read()
    {
        if (!Exists)
        {
            return null;
        }

        try
        {
            PemPair root = new(File.ReadAllText(files.RootPath), File.ReadAllText(files.RootKeyPath));

            using (X509Certificate2.CreateFromPem(root.CertificatePem, root.KeyPem))
            {
                return root;
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                $"DDT's root certificate {files.RootPath} cannot be used with its key {files.RootKeyPath}: {exception.Message} " +
                RunningAccount.AccessAdvice,
                exception);
        }
        catch (Exception exception) when (exception is IOException or CryptographicException)
        {
            // A new root would break every boot image, so DDT never makes one over an existing root on its own.
            throw new InvalidOperationException(
                $"DDT's root certificate {files.RootPath} cannot be used with its key {files.RootKeyPath}: {exception.Message} " +
                "Restore the key from a backup. Only if it is lost, delete both root files and the server certificate: DDT " +
                "then makes a new root, and every boot image has to be built again.",
                exception);
        }
    }

    public PemPair Create(DateTimeOffset now)
    {
        PemPair root = ServerCertificateAuthority.CreateRoot(now);

        // Write the key first. A root certificate without its key could never issue again.
        PemFiles.Write(files.RootKeyPath, root.KeyPem, isKey: true);
        PemFiles.Write(files.RootPath, root.CertificatePem, isKey: false);

        return root;
    }

    public bool Issued(X509Certificate2 certificate) => Of(certificate) is not null;

    // Returns the root's PEM if the certificate was issued from it, and null for an administrator's certificate.
    public string? Of(X509Certificate2 certificate)
    {
        if (!Exists)
        {
            return null;
        }

        string rootPem = File.ReadAllText(files.RootPath);
        using X509Certificate2 root = X509Certificate2.CreateFromPem(rootPem);

        return CertificateChains.ChainsTo(certificate, root) ? rootPem : null;
    }
}

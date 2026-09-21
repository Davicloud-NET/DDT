// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Certificates;

// Kestrel reads the certificate from disk, so a fresh deployment needs one before the first request. DDT makes its own
// root for it, because boot images pin the root: a certificate issued from it can change without a new boot image.
public sealed class ServerCertificates(CertificateFiles files, IReadOnlyList<string> names)
{
    public CertificateFiles Files { get; } = files ?? throw new ArgumentNullException(nameof(files));

    public IReadOnlyList<string> Names { get; } = names ?? throw new ArgumentNullException(nameof(names));

    public CertificateAction EnsureExists(DateTimeOffset now)
    {
        if (File.Exists(Files.CertificatePath) && File.Exists(Files.KeyPath))
        {
            return CertificateAction.Unchanged;
        }

        Directory.CreateDirectory(Files.Folder);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Files.KeyPath))!);

        CertificateAction action = CertificateAction.Issued;
        PemPair root;

        if (File.Exists(Files.RootPath) && File.Exists(Files.RootKeyPath))
        {
            root = new PemPair(File.ReadAllText(Files.RootPath), File.ReadAllText(Files.RootKeyPath));
        }
        else
        {
            root = ServerCertificateAuthority.CreateRoot(now);
            action = CertificateAction.Created;

            // The key first: a root certificate without its key could never issue again.
            PemFiles.Write(Files.RootKeyPath, root.KeyPem, isKey: true);
            PemFiles.Write(Files.RootPath, root.CertificatePem, isKey: false);
        }

        PemPair issued = ServerCertificateAuthority.Issue(root, Names, ServerNames.LocalAddresses(), now);
        PemFiles.Write(Files.KeyPath, issued.KeyPem, isKey: true);
        PemFiles.Write(Files.CertificatePath, issued.CertificatePem, isKey: false);

        return action;
    }
}

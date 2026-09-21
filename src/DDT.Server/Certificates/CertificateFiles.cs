// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Extensions.Configuration;

namespace DDT.Server.Certificates;

// DDT's root lives next to the server certificate, so whatever holds and backs up one holds the other.
public sealed record CertificateFiles(string CertificatePath, string KeyPath)
{
    public string Folder => Path.GetDirectoryName(Path.GetFullPath(CertificatePath))!;

    public string RootPath => Path.Combine(Folder, "ddt-root.pem");

    public string RootKeyPath => Path.Combine(Folder, "ddt-root-key.pem");

    // ddt.pem becomes ddt.previous.pem: the pair a renewal replaced, kept to go back to by hand.
    public string PreviousCertificatePath => Previous(CertificatePath);

    public string PreviousKeyPath => Previous(KeyPath);

    // The self-signed certificate boot images pinned before DDT had a root, kept until an administrator confirms that
    // every boot image was built again with the root.
    public string ReplacedAnchorPath => Path.Combine(Folder, "ddt-anchor.replaced.pem");

    // Taken by every DDT process that writes these files, or may have to, so two of them never renew at once.
    public string LockPath => Path.Combine(Folder, ".lock");

    // A PFX, or a key under a password, is managed by hand as before, so DDT only takes over two plain PEM files.
    public static CertificateFiles? FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? certificatePath = configuration["Kestrel:Certificates:Default:Path"];
        string? keyPath = configuration["Kestrel:Certificates:Default:KeyPath"];

        if (string.IsNullOrWhiteSpace(certificatePath)
            || string.IsNullOrWhiteSpace(keyPath)
            || !string.IsNullOrEmpty(configuration["Kestrel:Certificates:Default:Password"]))
        {
            return null;
        }

        return new CertificateFiles(certificatePath, keyPath);
    }

    private static string Previous(string path) =>
        Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(path))!,
            Path.GetFileNameWithoutExtension(path) + ".previous" + Path.GetExtension(path));
}

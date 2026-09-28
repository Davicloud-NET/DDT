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

    // ddt.pem becomes ddt.previous.pem. It keeps the pair a renewal replaced, so an admin can go back to it by hand.
    public string PreviousCertificatePath => Previous(CertificatePath);

    public string PreviousKeyPath => Previous(KeyPath);

    // Holds the self-signed certificate that older boot images pin. It's kept until an administrator confirms that
    // every boot image was built again with the root.
    public string ReplacedAnchorPath => Path.Combine(Folder, "ddt-anchor.replaced.pem");

    // Holds the deadline for a pair installed from the settings page. If the pair isn't confirmed by then, the pair
    // before it is restored.
    public string ProvisionalPath => Path.Combine(Folder, "ddt.provisional");

    // Every DDT process that writes these files, or may have to, takes this lock, so two processes never renew at once.
    public string LockPath => Path.Combine(Folder, ".lock");

    // DDT only takes over two plain PEM files. A PFX, or a key with a password, stays managed by hand.
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

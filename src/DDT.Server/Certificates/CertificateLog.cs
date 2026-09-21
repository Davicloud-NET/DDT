// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Certificates;

public static partial class CertificateLog
{
    public static void Checked(ILogger logger, ServerCertificates certificates, CertificateAction action)
    {
        ArgumentNullException.ThrowIfNull(certificates);

        string names = string.Join(", ", certificates.Names);

        switch (action)
        {
            case CertificateAction.Created:
                Created(logger, certificates.Files.CertificatePath, names, certificates.Files.RootPath, Fingerprint(certificates.Files.RootPath));
                break;
            case CertificateAction.Issued:
                Issued(logger, certificates.Files.CertificatePath, names);
                break;
        }
    }

    [LoggerMessage(
        EventId = 850,
        Level = LogLevel.Warning,
        Message = "Issued the server certificate {Path} for {Names} from DDT's new root certificate. Build boot images with " +
            "-RootCertificatePath {RootPath}, SHA-256 {RootSha256}, and trust that root in the browsers that manage DDT.")]
    private static partial void Created(ILogger logger, string path, string names, string rootPath, string rootSha256);

    [LoggerMessage(
        EventId = 853,
        Level = LogLevel.Information,
        Message = "Issued the server certificate {Path} for {Names} from DDT's root, because the files were missing")]
    private static partial void Issued(ILogger logger, string path, string names);

    private static string Fingerprint(string path)
    {
        using X509Certificate2 certificate = X509Certificate2.CreateFromPem(File.ReadAllText(path));

        return certificate.GetCertHashString(HashAlgorithmName.SHA256);
    }
}

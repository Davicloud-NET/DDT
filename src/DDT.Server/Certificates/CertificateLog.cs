// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Certificates;

// Automatic certificate work is only logged, never audited: no person did it.
public static partial class CertificateLog
{
    public static void Checked(ILogger logger, ServerCertificates certificates, CertificateCheck check)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(check);

        string path = certificates.Files.CertificatePath;
        string names = string.Join(", ", ServerNames.Of(check.Certificate));
        DateTimeOffset notAfter = new(check.Certificate.NotAfter);

        switch (check.Action)
        {
            case CertificateAction.Created:
                Created(logger, path, names, certificates.Files.RootPath, Fingerprint(certificates.Files.RootPath));
                break;
            case CertificateAction.Renewed:
                Renewed(logger, path, names, notAfter);
                break;
            case CertificateAction.Issued:
                Issued(logger, path, names, "the files were missing or did not load");
                break;
            case CertificateAction.Reissued:
                Issued(logger, path, names, "a configured name was missing from it");
                break;
            case CertificateAction.Reloaded:
                Reloaded(logger, path, notAfter);
                break;
            case CertificateAction.LoadFailed:
                LoadFailed(logger, path, check.Problem);
                break;
        }

        if (check.ExpiresSoon)
        {
            ExpiresSoon(logger, path, notAfter);
        }
    }

    [LoggerMessage(
        EventId = 850,
        Level = LogLevel.Warning,
        Message = "Issued the server certificate {Path} for {Names} from DDT's new root certificate. Build boot images with " +
            "-RootCertificatePath {RootPath}, SHA-256 {RootSha256}, and trust that root in the browsers that manage DDT.")]
    private static partial void Created(ILogger logger, string path, string names, string rootPath, string rootSha256);

    [LoggerMessage(EventId = 851, Level = LogLevel.Information, Message = "Renewed the server certificate {Path} for {Names}, valid until {NotAfter}")]
    private static partial void Renewed(ILogger logger, string path, string names, DateTimeOffset notAfter);

    [LoggerMessage(EventId = 853, Level = LogLevel.Information, Message = "Issued the server certificate {Path} for {Names} from DDT's root, because {Reason}")]
    private static partial void Issued(ILogger logger, string path, string names, string reason);

    [LoggerMessage(EventId = 854, Level = LogLevel.Information, Message = "Loaded the server certificate {Path} again after it changed, valid until {NotAfter}")]
    private static partial void Reloaded(ILogger logger, string path, DateTimeOffset notAfter);

    [LoggerMessage(
        EventId = 855,
        Level = LogLevel.Warning,
        Message = "The server certificate {Path} expires at {NotAfter}. DDT did not issue it and does not renew it, so replace it before then.")]
    private static partial void ExpiresSoon(ILogger logger, string path, DateTimeOffset notAfter);

    [LoggerMessage(
        EventId = 856,
        Level = LogLevel.Warning,
        Message = "The server certificate {Path} changed but does not load ({Problem}). The certificate loaded before stays in service.")]
    private static partial void LoadFailed(ILogger logger, string path, string? problem);

    [LoggerMessage(EventId = 857, Level = LogLevel.Error, Message = "Checking the server certificate failed")]
    public static partial void CheckFailed(ILogger logger, Exception exception);

    private static string Fingerprint(string path)
    {
        using X509Certificate2 certificate = X509Certificate2.CreateFromPem(File.ReadAllText(path));

        return certificate.GetCertHashString(HashAlgorithmName.SHA256);
    }
}

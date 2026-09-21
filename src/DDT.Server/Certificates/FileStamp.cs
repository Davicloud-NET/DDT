// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;

namespace DDT.Server.Certificates;

// Tells whether someone replaced the pair, without importing it every few minutes. By content rather than write time:
// a copy that keeps the original times, or two writes within one tick of the file system clock, would go unnoticed.
// Null means the file is missing.
internal sealed record FileStamp(string? CertificateSha256, string? KeySha256)
{
    public bool AnyExists => CertificateSha256 is not null || KeySha256 is not null;

    public static FileStamp Of(CertificateFiles files) => new(Hash(files.CertificatePath), Hash(files.KeyPath));

    private static string? Hash(string path)
    {
        try
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}

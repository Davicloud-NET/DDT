// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;

namespace DDT.Server.Certificates;

// Tells whether the pair was replaced, without importing it. It hashes the content because the write time can stay the
// same. A copy can keep the old times, and two writes can land within one tick of the file system clock. A null hash
// means the file is missing.
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

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Images;

namespace DDT.Core.Boot;

// Microsoft's third-party UEFI CAs from 2011 and 2023, known by the SHA-256 of their certificates. They sign the Linux
// shims. Stock PCs trust the 2011 CA, and the 2023 one after an update. Secured-core PCs and Hyper-V's Windows
// template trust neither by default.
public static class MicrosoftUefiCa
{
    public const string Thumbprint2011 = "48e99b991f57fc52f76149599bff0a58c47154229b9f8d603ac40d3500248507";
    public const string Thumbprint2023 = "f6124e34125bee3fe6d79a574eaa7b91c0e7bd9d929c1a321178efd611dad901";

    // Which of the two CAs a DER certificate is, or None.
    public static UefiCa Of(ReadOnlySpan<byte> certificate) => Convert.ToHexStringLower(SHA256.HashData(certificate)) switch
    {
        Thumbprint2011 => UefiCa.Microsoft2011,
        Thumbprint2023 => UefiCa.Microsoft2023,
        _ => UefiCa.None,
    };

    // Which of the two CAs the signature database db holds. Throws InvalidDataException if the database is malformed.
    public static UefiCa TrustedBy(ReadOnlySpan<byte> database) =>
        SignatureDatabase.Certificates(database).Aggregate(UefiCa.None, (trusted, certificate) => trusted | Of(certificate));

    // True when both are known and the firmware trusts none of the CAs that signed the boot file. The firmware then
    // won't start the file with Secure Boot on.
    public static bool Untrusted(UefiCa? trusted, UefiCa? signedUnder) =>
        trusted is { } firmware && signedUnder is { } file && file != UefiCa.None && (firmware & file) == UefiCa.None;

    // "Microsoft's third-party UEFI CA 2011", "... CA 2023" or "... CAs 2011 and 2023".
    public static string Describe(UefiCa? cas) => cas switch
    {
        UefiCa.Microsoft2011 => "Microsoft's third-party UEFI CA 2011",
        UefiCa.Microsoft2023 => "Microsoft's third-party UEFI CA 2023",
        UefiCa.Microsoft2011 | UefiCa.Microsoft2023 => "Microsoft's third-party UEFI CAs 2011 and 2023",
        _ => "Microsoft's third-party UEFI CA",
    };
}

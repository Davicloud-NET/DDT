// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DDT.Core.Applications.Msix;

// A package family name is the identity name, an underscore and the publisher id, which Windows derives from the
// publisher: the first 8 bytes of SHA-256 over its UTF-16LE text, as 13 characters of its own base 32.
public static partial class PackageFamilyNames
{
    public const int PublisherIdLength = 13;
    public const string Example = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";

    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    public static string PublisherId(string publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);

        byte[] hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));
        ulong bits = 0;

        for (int index = 0; index < 8; index++)
        {
            bits = (bits << 8) | hash[index];
        }

        // 64 bits and one zero bit make 13 groups of 5.
        char[] id = new char[PublisherIdLength];

        for (int group = 0; group < PublisherIdLength; group++)
        {
            int shift = 59 - (group * 5);
            ulong value = shift >= 0 ? bits >> shift : bits << -shift;
            id[group] = Alphabet[(int)(value & 0x1F)];
        }

        return new string(id);
    }

    public static string Of(string name, string publisher)
    {
        ArgumentNullException.ThrowIfNull(name);

        return $"{name}_{PublisherId(publisher)}";
    }

    public static bool IsFamilyName([NotNullWhen(true)] string? value) => value is not null && FamilyName().IsMatch(value);

    public static bool IsIdentityName([NotNullWhen(true)] string? value) => value is not null && IdentityName().IsMatch(value);

    // A package full name is Name_Version_Architecture_ResourceId_PublisherId, and a name has no underscore.
    public static bool IsOfFamily(string fullName, string familyName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        ArgumentNullException.ThrowIfNull(familyName);

        int split = familyName.LastIndexOf('_');

        if (split <= 0)
        {
            return false;
        }

        string name = familyName[..(split + 1)];
        string publisherId = familyName[split..];

        return fullName.Length > name.Length + publisherId.Length
            && fullName.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && fullName.EndsWith(publisherId, StringComparison.OrdinalIgnoreCase);
    }

    // The manifest schema's ST_PackageName.
    [GeneratedRegex(@"^[A-Za-z0-9.\-]{3,50}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityName();

    [GeneratedRegex(@"^[A-Za-z0-9.\-]{3,50}_[0-9a-hjkmnp-tv-z]{13}\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex FamilyName();
}

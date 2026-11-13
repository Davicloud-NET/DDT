// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Server.Applications.Installers;

// The SetupIDs that start setup-0.bin, from 5.3.10 on, when CreateUninstallRegKey and Uninstallable joined the header.
// Up to 6.4.0.1 from innoextract's version.cpp, later ones from the SetupID in Shared.Struct.pas of each issrc tag.
// Any other ID, 5.5.8 (u) of an unofficial build among them, is unknown.
internal static class InnoSetupVersions
{
    public const int IdLength = 64;

    private static readonly InnoSetupLayout s_ansi = new(Unicode: false, EncryptionHeader: false, LongBlockSize: false, CreateKeyIndex: 24);
    private static readonly InnoSetupLayout s_unicode = s_ansi with { Unicode = true };
    private static readonly InnoSetupLayout s_encrypted = s_unicode with { EncryptionHeader = true };
    private static readonly InnoSetupLayout s_long = s_encrypted with { LongBlockSize = true };

    // 7.1 dropped BaseFilename from the header.
    private static readonly InnoSetupLayout s_withoutBaseFilename = s_long with { CreateKeyIndex = 23 };

    private static readonly Dictionary<string, InnoSetupLayout> s_layouts = new(StringComparer.Ordinal)
    {
        ["5.3.10"] = s_ansi,
        ["5.3.10) (u"] = s_unicode,
        ["5.4.2"] = s_ansi,
        ["5.4.2) (u"] = s_unicode,
        ["5.5.0"] = s_ansi,
        ["5.5.0) (u"] = s_unicode,
        ["5.5.6"] = s_ansi,
        ["5.5.6) (u"] = s_unicode,
        ["5.5.7"] = s_ansi,
        ["5.5.7) (u"] = s_unicode,
        ["5.5.7) (U"] = s_unicode,
        ["5.6.0"] = s_ansi,
        ["5.6.0) (u"] = s_unicode,
        ["5.6.2"] = s_ansi,
        ["5.6.2) (u"] = s_unicode,
        ["6.0.0) (u"] = s_unicode,
        ["6.1.0) (u"] = s_unicode,
        ["6.3.0"] = s_unicode,
        ["6.4.0"] = s_unicode,
        ["6.4.0.1"] = s_unicode,
        ["6.4.2"] = s_unicode,
        ["6.4.3"] = s_unicode,
        ["6.5.0"] = s_encrypted,
        ["6.5.2"] = s_encrypted,
        ["6.6.0"] = s_encrypted,
        ["6.6.1"] = s_encrypted,
        ["6.7.0"] = s_long,
        ["7.0.0.1"] = s_long,
        ["7.0.0.3"] = s_long,
        ["7.1.1"] = s_withoutBaseFilename,
    };

    // The ID is "Inno Setup Setup Data (<version>)", padded with zeros.
    public static InnoSetupLayout? Of(ReadOnlySpan<byte> id)
    {
        const string prefix = "Inno Setup Setup Data (";

        if (id.Length != IdLength)
        {
            return null;
        }

        int end = id.IndexOf((byte)0);
        string text = Encoding.ASCII.GetString(end < 0 ? id : id[..end]);

        return end >= 0 && id[end..].IndexOfAnyExcept((byte)0) < 0 && text.StartsWith(prefix, StringComparison.Ordinal) && text.EndsWith(')')
            ? s_layouts.GetValueOrDefault(text[prefix.Length..^1])
            : null;
    }
}

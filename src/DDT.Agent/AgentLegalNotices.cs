// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// The agent reaches machines on its own, in a boot image or as an update, so it carries the legal texts that apply
// to it. The start-up lines are the GPL's Appropriate Legal Notices. The LGPL wants wimlib's copyright among them.
public static class AgentLegalNotices
{
    // Additional term 1 in NOTICE requires this exact text.
    public const string AttributionNotice = LegalTexts.AttributionNotice;

    // wimlib's COPYING names no holder. This is the line wimlib's own programs print with their version.
    public const string WimlibCopyright = "Copyright 2012-2023 Eric Biggers";

    public static IReadOnlyList<string> StartupLines { get; } =
    [
        AttributionNotice,
        LegalTexts.NoWarranty,
        LegalTexts.FreeSoftware,
        $"It uses wimlib, {WimlibCopyright}, which is under the GNU Lesser General Public License, version 3 or later.",
        LegalTexts.HowToReadTheLicences,
    ];

    // Embedded under these names by DDT.Agent.csproj, and printed in this order.
    public static IReadOnlyList<string> Files { get; } =
    [
        "NOTICE",
        "LICENSE",
        "THIRD-PARTY-NOTICES.md",
        "licenses/dotnet/LICENSE.TXT",
        "licenses/dotnet/THIRD-PARTY-NOTICES.TXT",
        "licenses/wimlib/COPYING",
        "licenses/wimlib/COPYING.LGPLv3",
        "licenses/wimlib/COPYING.GPLv3",
        "licenses/wimlib/COPYING.MIT",
        "licenses/wimlib/COPYING.libdivsufsort-lite",
        "licenses/wimlib/COPYING.MinGW-w64-runtime.txt",
        "licenses/zstd/ZstdSharp-LICENSE",
        "licenses/zstd/LICENSE",
    ];

    // After the lists it prints, which are made in the order they stand here.
    private static readonly LegalTexts s_texts = new(typeof(AgentLegalNotices).Assembly, StartupLines, Files);

    public static void WriteStartupNotices(TextWriter output) => s_texts.WriteStartupNotices(output);

    public static void WriteLicenses(TextWriter output) => s_texts.WriteLicenses(output);

    public static string ReadText(string file) => s_texts.ReadText(file);
}

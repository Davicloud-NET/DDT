// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// The legal texts of the agent for Linux, which reaches machines on its own as one file. The start-up lines are the
// GPL's Appropriate Legal Notices. Nothing it is built from asks for a line of its own among them. OpenSSL, musl
// and the compiler's support code are linked into the file, so their texts are in it too.
public static class LinuxLegalNotices
{
    public static IReadOnlyList<string> StartupLines { get; } =
    [
        LegalTexts.AttributionNotice,
        LegalTexts.NoWarranty,
        LegalTexts.FreeSoftware,
        LegalTexts.HowToReadTheLicences,
    ];

    // Embedded under these names by DDT.Agent.Linux.csproj, and printed in this order.
    public static IReadOnlyList<string> Files { get; } =
    [
        "NOTICE",
        "LICENSE",
        "THIRD-PARTY-NOTICES.md",
        "licenses/dotnet/LICENSE.TXT",
        "licenses/dotnet/THIRD-PARTY-NOTICES.TXT",
        "licenses/zstd/ZstdSharp-LICENSE",
        "licenses/zstd/LICENSE",
        "licenses/openssl/LICENSE.txt",
        "licenses/musl/COPYRIGHT",
        "licenses/gcc/COPYING.RUNTIME",
    ];

    // After the lists it prints, which are made in the order they stand here.
    private static readonly LegalTexts s_texts = new(typeof(LinuxLegalNotices).Assembly, StartupLines, Files);

    public static void WriteStartupNotices(TextWriter output) => s_texts.WriteStartupNotices(output);

    public static void WriteLicenses(TextWriter output) => s_texts.WriteLicenses(output);

    public static string ReadText(string file) => s_texts.ReadText(file);
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Windows packages and the Languages and Optional Features ISO.

    public static readonly MessageTemplate UploadWindowsPackageFile = Define(
        "upload.windowsPackageFile",
        "Upload a .msu or .cab file, a zip of them, or the Languages and Optional Features ISO.");

    public static readonly MessageTemplate PackageWindowsFile = Define(
        "package.windowsFile",
        "{entry} is no .msu or .cab file. A Windows package holds nothing else.");

    public static readonly MessageTemplate PackageWindowsFolder = Define(
        "package.windowsFolder",
        "{entry} lies in a folder. Put the .msu and .cab files at the top of the zip.");

    public static readonly MessageTemplate PackageWindowsEmpty = Define("package.windowsEmpty", "The zip holds no .msu or .cab file.");

    public static readonly MessageTemplate IsoNoLanguages = Define(
        "iso.noLanguages",
        "This ISO has no LanguagesAndOptionalFeatures folder, so it is not the Languages and Optional Features ISO.");

    public static readonly MessageTemplate ImportNotAnIso = Define(
        "import.notAnIso",
        "{path} is no ISO in one of this server's import folders.");

    public static readonly MessageTemplate ImportSuffix = Define(
        "import.suffix",
        "The end of the names has at most {max} characters and no control characters.");
}

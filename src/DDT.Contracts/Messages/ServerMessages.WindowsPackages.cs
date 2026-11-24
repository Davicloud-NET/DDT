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
        "{entry} is not a .msu or .cab file. A Windows package holds nothing else.");

    public static readonly MessageTemplate PackageWindowsFolder = Define(
        "package.windowsFolder",
        "{entry} lies in a folder. Put the .msu and .cab files at the top of the zip.");

    public static readonly MessageTemplate PackageWindowsNoTargets = Define(
        "package.windowsNoTargets",
        "A Windows package goes to the machines whose sequence names it, not by their model, so it has no targets.");

    public static readonly MessageTemplate PackageBuilds = Define("package.builds", "Enter up to {max} Windows builds.");

    public static readonly MessageTemplate PackageBuild = Define("package.build", "A Windows build is a number such as 26100.");

    public static readonly MessageTemplate PackageBuildsWindowsOnly = Define("package.buildsWindowsOnly", "Only a Windows package has builds.");

    public static readonly MessageTemplate PackageWindowsEmpty = Define("package.windowsEmpty", "The zip holds no .msu or .cab file.");

    public static readonly MessageTemplate IsoNoLanguages = Define(
        "iso.noLanguages",
        "This ISO has no LanguagesAndOptionalFeatures folder, so it is not the Languages and Optional Features ISO.");

    public static readonly MessageTemplate ImportNotAnIso = Define(
        "import.notAnIso",
        "{path} is not an ISO in one of this server's import folders.");

    public static readonly MessageTemplate ImportSuffix = Define(
        "import.suffix",
        "The end of the names has at most {max} characters and no control characters.");
}

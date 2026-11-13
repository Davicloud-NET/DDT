// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Core.Applications.Msix;

// Finds the packages in an application's files, wherever they are: exactly one is not a framework package and is the
// main one, the others are its dependencies. A Store licence is a .xml at the root whose root element is License.
// The server reads the files when they are saved and the agent before DISM, so both decide the same.
public static class MsixLayouts
{
    public const int MaxPackages = 64;

    private static readonly string[] s_extensions = [".msix", ".appx", ".msixbundle", ".appxbundle"];

    public static bool IsPackage(string path) =>
        s_extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    // files are relative paths with forward slashes. manifestOf reads a package's manifest, or null when it cannot;
    // isLicense reads a .xml at the root.
    public static MsixLayoutResult Of(IReadOnlyList<string> files, Func<string, MsixManifest?> manifestOf, Func<string, bool> isLicense)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(manifestOf);
        ArgumentNullException.ThrowIfNull(isLicense);

        string[] packages = [.. files.Where(IsPackage).Order(StringComparer.OrdinalIgnoreCase)];

        if (packages.Length == 0)
        {
            return MsixLayoutResult.Refused(ServerMessages.ApplicationMsixNone.With()) with { HasPackages = false };
        }

        if (packages.Length > MaxPackages)
        {
            return MsixLayoutResult.Refused(ServerMessages.ApplicationMsixTooMany.With("max", MaxPackages));
        }

        List<MsixPackageFile> read = [];

        foreach (string package in packages)
        {
            if (manifestOf(package) is not { } manifest)
            {
                return MsixLayoutResult.Refused(ServerMessages.ApplicationMsixUnreadable.With("file", package));
            }

            read.Add(new MsixPackageFile(package, manifest));
        }

        return Arranged(read, [.. files.Where(IsRootXml).Order(StringComparer.OrdinalIgnoreCase).Where(isLicense)]);
    }

    private static MsixLayoutResult Arranged(List<MsixPackageFile> packages, string[] licenses)
    {
        MsixPackageFile[] mains = [.. packages.Where(package => !package.Manifest.IsFramework)];

        if (mains.Length == 0)
        {
            return MsixLayoutResult.Refused(ServerMessages.ApplicationMsixOnlyFrameworks.With());
        }

        if (mains.Length > 1)
        {
            return MsixLayoutResult.Refused(ServerMessages.ApplicationMsixTwoMains.With("packages", string.Join(", ", mains.Select(main => main.Path))));
        }

        if (licenses.Length > 1)
        {
            return MsixLayoutResult.Refused(ServerMessages.ApplicationMsixTwoLicenses.With("files", string.Join(", ", licenses)));
        }

        return MsixLayoutResult.Of(new MsixLayout(mains[0], [.. packages.Where(package => package != mains[0])], licenses.FirstOrDefault()));
    }

    private static bool IsRootXml(string path) =>
        path.IndexOfAny(['/', '\\']) < 0 && path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
}

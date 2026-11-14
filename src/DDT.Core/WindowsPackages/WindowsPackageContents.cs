// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Packages;

namespace DDT.Core.WindowsPackages;

// Sums up a Windows package from the names of its files, for the library to show and filter by.
public static class WindowsPackageContents
{
    public static WindowsPackageFacts Of(IEnumerable<string> fileNames)
    {
        ArgumentNullException.ThrowIfNull(fileNames);

        List<WindowsPackageFile> files =
        [
            .. fileNames.Select(name => WindowsPackageNames.Read(Path.GetFileName(name))).OfType<WindowsPackageFile>(),
        ];
        bool updates = files.Any(file => file.Kind == WindowsPackageFileKind.Update);
        bool cabinets = files.Any(file => file.Kind != WindowsPackageFileKind.Update);

        WindowsPackageContent content = (updates, cabinets) switch
        {
            (true, true) => WindowsPackageContent.Mixed,
            (true, false) => WindowsPackageContent.Update,
            _ when files.Any(file => file.Kind == WindowsPackageFileKind.LanguagePack) => WindowsPackageContent.Language,
            _ => WindowsPackageContent.Features,
        };

        string?[] architectures = [.. files.Select(file => file.Architecture).Distinct(StringComparer.Ordinal)];

        return new WindowsPackageFacts(
            content,
            [.. files.Select(file => file.Language).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)],
            architectures is [{ } architecture] ? architecture : null,
            [.. files.Select(file => file.Kb).OfType<int>().Distinct().OrderDescending()],
            []);
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.WindowsPackages;

namespace DDT.Agent.Servicing;

// The DISM calls for the step's packages in DDT's order: each language pack alone, then the other cabinets together,
// then each update package, lowest KB first. A cumulative update then also updates the languages added before it.
// Within a group the step's order holds.
public static class ServicingPlan
{
    // Of the 8,000 characters a DISM command line may have, the rest is for the image, scratch and log paths.
    public const int MaxPackagePathCharacters = 7_000;

    public static IReadOnlyList<ServicingCall> Of(IReadOnlyList<UnpackedPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        List<(UnpackedPackage Package, string File, WindowsPackageFile Name)> files = [];

        foreach (UnpackedPackage package in packages)
        {
            foreach (string file in Directory.EnumerateFiles(package.Folder).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (WindowsPackageNames.Read(Path.GetFileName(file)) is { } name)
                {
                    files.Add((package, file, name));
                }
            }
        }

        List<ServicingCall> calls =
        [
            .. files
                .Where(file => file.Name.Kind == WindowsPackageFileKind.LanguagePack)
                .Select(file => new ServicingCall(false, [file.File], [file.Package])),
        ];
        calls.AddRange(Batches(files.Where(file => file.Name.Kind is WindowsPackageFileKind.LanguageFeature or WindowsPackageFileKind.Feature)));
        calls.AddRange(files
            .Where(file => file.Name.Kind == WindowsPackageFileKind.Update)
            .GroupBy(file => file.Package)
            .Select(update => update.OrderBy(file => file.Name.Kb ?? 0).ThenBy(file => file.File, StringComparer.OrdinalIgnoreCase).Last())
            .OrderBy(target => target.Name.Kb ?? 0)
            .Select(target => new ServicingCall(true, [target.File], [target.Package])));

        return calls;
    }

    private static IEnumerable<ServicingCall> Batches(IEnumerable<(UnpackedPackage Package, string File, WindowsPackageFile Name)> cabinets)
    {
        List<(UnpackedPackage Package, string File)> batch = [];
        int length = 0;

        foreach ((UnpackedPackage package, string file, _) in cabinets)
        {
            int added = " /PackagePath:".Length + file.Length;

            if (batch.Count > 0 && length + added > MaxPackagePathCharacters)
            {
                yield return Call(batch);
                batch = [];
                length = 0;
            }

            batch.Add((package, file));
            length += added;
        }

        if (batch.Count > 0)
        {
            yield return Call(batch);
        }
    }

    private static ServicingCall Call(List<(UnpackedPackage Package, string File)> batch) =>
        new(false, [.. batch.Select(item => item.File)], [.. batch.Select(item => item.Package).Distinct()]);
}

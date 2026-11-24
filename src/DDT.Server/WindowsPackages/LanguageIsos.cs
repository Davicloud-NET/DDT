// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.IO.Compression;
using DDT.Core.WindowsPackages;
using DDT.Server.Images;
using DiscUtils;

namespace DDT.Server.WindowsPackages;

// Reads Microsoft's Languages and Optional Features ISO: its cabinets lie side by side in LanguagesAndOptionalFeatures.
// A language gets its language pack, its language features and the fonts its script needs.
public static class LanguageIsos
{
    private const string Folder = "LanguagesAndOptionalFeatures";
    private const int CopyBufferBytes = 1024 * 1024;

    // A fixed time and no compression make the same cabinets give the same zip, which the library then keeps once.
    private static readonly DateTimeOffset s_entryTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // The font cabinets of the ISO's languages whose script Windows does not carry by itself.
    private static readonly Dictionary<string, string> s_fonts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ar"] = "Arab",
        ["fa"] = "Arab",
        ["he"] = "Hebr",
        ["ja"] = "Jpan",
        ["ko"] = "Kore",
        ["th"] = "Thai",
        ["zh-CN"] = "Hans",
        ["zh-SG"] = "Hans",
        ["zh-TW"] = "Hant",
        ["zh-HK"] = "Hant",
    };

    public const int MaxSuffixLength = 64;

    // The names of the cabinets in the folder. Null for an ISO without it.
    public static Task<IReadOnlyList<string>?> ListAsync(string iso, CancellationToken cancellationToken) =>
        Task.Run(() => List(iso), cancellationToken);

    // The languages a client or server language pack is there for. A language interface pack needs another language
    // under it, so it is left out.
    public static IReadOnlyList<string> Languages(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        return
        [
            .. names
                .Where(name => !name.Contains("-Lip-", StringComparison.OrdinalIgnoreCase))
                .Select(WindowsPackageNames.Read)
                .Where(file => file is { Kind: WindowsPackageFileKind.LanguagePack })
                .Select(file => file?.Language)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal),
        ];
    }

    // The language pack, the language's features and its fonts, in the order the ISO lists them.
    public static IReadOnlyList<string> FilesOf(IReadOnlyList<string> names, string language)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(language);

        string? fonts = FontsOf(language) is { } script ? $"Microsoft-Windows-LanguageFeatures-Fonts-{script}-Package~" : null;

        return
        [
            .. names.Where(name =>
                (fonts is not null && name.StartsWith(fonts, StringComparison.OrdinalIgnoreCase))
                || (!name.Contains("-Lip-", StringComparison.OrdinalIgnoreCase)
                    && WindowsPackageNames.Read(name) is { Kind: WindowsPackageFileKind.LanguagePack or WindowsPackageFileKind.LanguageFeature } file
                    && string.Equals(file.Language, language, StringComparison.OrdinalIgnoreCase))),
        ];
    }

    // "(26100)" for the name Microsoft gives the ISO, such as 26100.1.240331-1435.ge_release_amd64fre_CLIENT_LOF_PACKAGES_OEM.iso.
    public static string Suffix(string iso) => Build(iso) is { } build ? string.Create(CultureInfo.InvariantCulture, $"({build})") : "";

    // The Windows build the ISO's name starts with, or null.
    public static int? Build(string iso)
    {
        ArgumentNullException.ThrowIfNull(iso);

        string name = Path.GetFileName(iso);
        int dot = name.IndexOf('.', StringComparison.Ordinal);

        return dot == 5 && name[..dot].All(char.IsAsciiDigit) ? int.Parse(name[..dot], CultureInfo.InvariantCulture) : null;
    }

    // Writes the named cabinets into a zip without compression, reporting the bytes done and their total.
    public static Task ZipAsync(string iso, IReadOnlyList<string> names, string target, Action<long, long>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Zip(iso, names, target, progress, cancellationToken), cancellationToken);

    private static IReadOnlyList<string>? List(string iso)
    {
        using FileStream file = new(iso, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using DiscFileSystem disc = IsoImages.Open(file);

        return FolderOf(disc) is { } folder ? [.. disc.GetFiles(folder).Select(IsoImages.NameOf)] : null;
    }

    private static void Zip(string iso, IReadOnlyList<string> names, string target, Action<long, long>? progress, CancellationToken cancellationToken)
    {
        using FileStream file = new(iso, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using DiscFileSystem disc = IsoImages.Open(file);
        string folder = FolderOf(disc) ?? throw new InvalidDataException($"The ISO has no {Folder} folder.");
        Dictionary<string, string> paths = disc.GetFiles(folder).ToDictionary(IsoImages.NameOf, StringComparer.OrdinalIgnoreCase);
        string[] chosen = [.. names.Select(name => paths.GetValueOrDefault(name) ?? throw new InvalidDataException($"The ISO has no {name}."))];
        long total = chosen.Sum(path => disc.GetFileLength(path));
        long done = 0;
        byte[] buffer = new byte[CopyBufferBytes];

        using FileStream output = new(target, FileMode.Create, FileAccess.ReadWrite, FileShare.None, CopyBufferBytes);
        using ZipArchive archive = new(output, ZipArchiveMode.Create);

        foreach (string path in chosen)
        {
            ZipArchiveEntry entry = archive.CreateEntry(IsoImages.NameOf(path), CompressionLevel.NoCompression);
            entry.LastWriteTime = s_entryTime;
            entry.ExternalAttributes = 0;

            using Stream source = disc.OpenFile(path, FileMode.Open, FileAccess.Read);
            using Stream data = entry.Open();
            int read;

            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                data.Write(buffer, 0, read);
                done += read;
                progress?.Invoke(done, total);
            }
        }
    }

    private static string? FolderOf(DiscFileSystem disc) =>
        disc.GetDirectories(string.Empty).FirstOrDefault(directory => IsoImages.Named(directory, Folder));

    private static string? FontsOf(string language) =>
        s_fonts.GetValueOrDefault(language) ?? s_fonts.GetValueOrDefault(language.Split('-')[0]);
}

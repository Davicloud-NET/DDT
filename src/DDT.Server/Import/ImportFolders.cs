// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Import;
using DDT.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DDT.Server.Import;

// The folders on the server that the library imports from: "import" in the store, and what DDT:ImportFolders names.
// An administrator's session must not make the server read any file it can reach, so nothing else is read.
public sealed class ImportFolders(IOptions<DdtOptions> options)
{
    private const int MaxFiles = 500;
    private const int MaxDepth = 4;

    private static readonly string[] s_extensions = [".wim", ".esd", ".iso"];

    private static readonly StringComparison s_names = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // In the store, so every server has one without configuration
    public string Default => Path.GetFullPath(Path.Combine(options.Value.StorePath, "import"));

    public IReadOnlyList<ImportFolder> Folders()
    {
        Directory.CreateDirectory(Default);

        return
        [
            new ImportFolder(Default, true),
            .. options.Value.ImportFolders
                .Where(folder => !string.IsNullOrWhiteSpace(folder) && Path.IsPathFullyQualified(folder))
                .Select(folder => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)))
                .Where(folder => !string.Equals(folder, Default, s_names))
                .Distinct(StringComparer.FromComparison(s_names))
                .Select(folder => new ImportFolder(folder, false)),
        ];
    }

    // The full path when it lies in an import folder and no link below that folder leads out of it. Null otherwise.
    public string? Allowed(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        return Folders().Any(folder => Within(folder.Path, full) && !LeavesByLink(folder.Path, full)) ? full : null;
    }

    public static bool IsImage(string path) => s_extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // MDT deployment shares: an import folder, or a folder right below one, with MDT's list of operating systems.
    public IReadOnlyList<string> Shares()
    {
        List<string> shares = [];

        foreach (ImportFolder folder in Folders().Where(folder => Directory.Exists(folder.Path)))
        {
            try
            {
                shares.AddRange(new[] { folder.Path }
                    .Concat(Directory.EnumerateDirectories(folder.Path, "*", Options(0)))
                    .Where(MdtShare.IsShare));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be listed offers nothing
            }
        }

        return shares;
    }

    // The WIM, ESD and ISO files in the import folders. A share's own files are left to the share's import.
    public IReadOnlyList<ImportFile> Files()
    {
        IReadOnlyList<string> shares = Shares();
        List<ImportFile> files = [];

        foreach (ImportFolder folder in Folders().Where(folder => Directory.Exists(folder.Path)))
        {
            try
            {
                files.AddRange(new DirectoryInfo(folder.Path)
                    .EnumerateFiles("*", Options(MaxDepth))
                    .Where(file => IsImage(file.Name) && !shares.Any(share => Within(share, file.FullName)))
                    .Take(MaxFiles - files.Count)
                    .Select(file => new ImportFile(file.FullName, file.Name, file.Length, file.LastWriteTimeUtc)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The same: nothing to offer from there
            }
        }

        return [.. files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)];
    }

    public static bool Within(string folder, string path) =>
        path.Equals(folder, s_names) || path.StartsWith(folder + Path.DirectorySeparatorChar, s_names);

    private static EnumerationOptions Options(int depth) => new()
    {
        RecurseSubdirectories = depth > 0,
        MaxRecursionDepth = depth,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
    };

    // The import folder itself may be a link, since configuration names it. What lies below it may not.
    private static bool LeavesByLink(string folder, string path)
    {
        for (string? part = path; part is not null && !part.Equals(folder, s_names); part = Path.GetDirectoryName(part))
        {
            FileSystemInfo entry = Directory.Exists(part) ? new DirectoryInfo(part) : new FileInfo(part);

            if (entry.Exists && entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }
}

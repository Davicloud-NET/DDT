// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Applications.Msix;

// The layout of an application's files unpacked into a folder, as the agent has them.
public static class MsixFolderLayout
{
    public static MsixLayoutResult Read(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);

        string[] files = Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'))]
            : [];

        return MsixLayouts.Of(files, file => Open(folder, file, package => MsixManifestReader.Read(package)), file => Open(folder, file, MsixXml.IsLicense));
    }

    public static string FullPath(string folder, string file) => Path.Combine(folder, file.Replace('/', Path.DirectorySeparatorChar));

    private static T Open<T>(string folder, string file, Func<Stream, T> read)
    {
        using FileStream data = new(FullPath(folder, file), FileMode.Open, FileAccess.Read, FileShare.Read);

        return read(data);
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Pxe;

namespace DDT.Host.Helper;

// Puts a finished build into the boot directory as a build of its own and makes it the one that is served. The web
// server's account can write there, so the helper follows no link on the way: a junction would have SYSTEM write
// wherever it points.
public static class BuildPublisher
{
    public static void Publish(string built, string bootDirectory, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(built);
        ArgumentException.ThrowIfNullOrEmpty(bootDirectory);

        string builds = Path.Combine(bootDirectory, BootBuilds.FolderName);
        string target = BootBuilds.FolderOf(bootDirectory, name);

        foreach (string folder in new[] { bootDirectory, builds })
        {
            Directory.CreateDirectory(folder);
            RefuseLink(folder);
        }

        // Copied, not moved: files made here get the store's permissions, which let the web server serve them.
        Directory.CreateDirectory(target);
        RefuseLink(target);
        Copy(new DirectoryInfo(built), target);
        BootBuilds.SetCurrent(bootDirectory, name);
    }

    private static void Copy(DirectoryInfo source, string target)
    {
        foreach (FileInfo file in source.EnumerateFiles())
        {
            file.CopyTo(Path.Combine(target, file.Name), overwrite: false);
        }

        foreach (DirectoryInfo child in source.EnumerateDirectories())
        {
            string folder = Path.Combine(target, child.Name);
            Directory.CreateDirectory(folder);
            RefuseLink(folder);
            Copy(child, folder);
        }
    }

    private static void RefuseLink(string folder)
    {
        if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException($"{folder} is a link, and the helper writes only into real folders of the store.");
        }
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

// Replacing a file that a download may still read. Linux renames over an open file; Windows refuses. So readers open
// with FileShare.Delete (OpenRead), and the old file steps aside under another name and goes when its last reader
// closes it.
internal static class FileReplacement
{
    private const string AsideExtension = ".old";

    public static string TemporaryFor(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        return $"{path}.{Guid.NewGuid():N}.upload";
    }

    // FileShare.Write as PhysicalFile had it: a build may overwrite a configured file in place, and whoever downloads it
    // checks its hash.
    public static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public static void Replace(string temporary, string path)
    {
        try
        {
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (OperatingSystem.IsWindows() && exception is IOException or UnauthorizedAccessException)
        {
            string aside = MoveAside(path);
            File.Move(temporary, path);
            DeleteOrLeave(aside);
        }

        Sweep(path);
    }

    // Aside first: a deleted file that's still open keeps its name on some file systems, and the next upload needs it.
    public static void Delete(string path)
    {
        if (File.Exists(path))
        {
            DeleteOrLeave(OperatingSystem.IsWindows() ? MoveAside(path) : path);
        }

        Sweep(path);
    }

    private static string MoveAside(string path)
    {
        string aside = $"{path}.{Guid.NewGuid():N}{AsideExtension}";
        File.Move(path, aside);

        return aside;
    }

    // What an earlier replacement couldn't delete yet
    private static void Sweep(string path)
    {
        string folder = Path.GetDirectoryName(path)!;

        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (string aside in Directory.EnumerateFiles(folder, $"{Path.GetFileName(path)}.*{AsideExtension}"))
        {
            DeleteOrLeave(aside);
        }
    }

    private static void DeleteOrLeave(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Still read without FileShare.Delete. The next replacement tries again.
        }
    }
}

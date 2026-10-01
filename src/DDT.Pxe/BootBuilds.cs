// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// The boot directory holds one build's files, as a copy by hand leaves them, or a folder per build below "builds"
// with the file "current" naming the one that is served. A new build becomes current in one rename, so no machine
// gets files of two builds, and the build before stays to go back to.
public static class BootBuilds
{
    public const string FolderName = "builds";
    public const string MarkerName = "current";

    private const int MaxNameLength = 64;

    public static string FolderOf(string bootDirectory, string build) => Path.Combine(bootDirectory, FolderName, build);

    // Where the boot files are served from now.
    public static string Serving(string bootDirectory) =>
        Current(bootDirectory) is { } build ? FolderOf(bootDirectory, build) : bootDirectory;

    // Null while the boot directory itself holds the files.
    public static string? Current(string bootDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(bootDirectory);

        try
        {
            string marker = Path.Combine(bootDirectory, MarkerName);

            if (!File.Exists(marker))
            {
                return null;
            }

            // Shared for deleting, so the rename that names the next build never waits for a reader
            using FileStream stream = new(marker, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream);
            char[] text = new char[MaxNameLength + 2];
            string name = new string(text, 0, reader.ReadBlock(text, 0, text.Length)).Trim();

            return IsBuildName(name) && Directory.Exists(FolderOf(bootDirectory, name)) ? name : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Null serves the boot directory itself again.
    public static void SetCurrent(string bootDirectory, string? build)
    {
        ArgumentException.ThrowIfNullOrEmpty(bootDirectory);

        string marker = Path.Combine(bootDirectory, MarkerName);

        if (build is null)
        {
            File.Delete(marker);

            return;
        }

        if (!IsBuildName(build))
        {
            throw new ArgumentException($"{build} is not the name of a build.", nameof(build));
        }

        string next = marker + ".next";
        File.WriteAllText(next, build);
        Replace(next, marker);
    }

    // Windows refuses to rename over a file that is open. A reader has it for an instant, so waiting comes first: moving
    // the old name aside would leave a moment without any.
    private static void Replace(string next, string marker)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(next, marker, overwrite: true);

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < 20)
            {
                Thread.Sleep(10);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                string aside = $"{marker}.old-{Guid.NewGuid():N}";
                File.Move(marker, aside);
                File.Move(next, marker);
                File.Delete(aside);

                return;
            }
        }
    }

    public static IReadOnlyList<string> List(string bootDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(bootDirectory);

        DirectoryInfo builds = new(Path.Combine(bootDirectory, FolderName));

        return builds.Exists
            ? [.. builds.EnumerateDirectories().Select(build => build.Name).Where(IsBuildName).Order(StringComparer.Ordinal)]
            : [];
    }

    // A folder name and nothing that leaves the builds folder
    public static bool IsBuildName(string name) =>
        name is { Length: > 0 and <= MaxNameLength }
        && char.IsAsciiLetterOrDigit(name[0])
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

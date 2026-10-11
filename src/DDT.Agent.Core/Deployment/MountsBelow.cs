// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Agent.Deployment;

// What Linux has mounted on a folder or below it. Deleting the folder with all it holds would delete the files of what
// a script left mounted there, such as a disk or a share.
internal static class MountsBelow
{
    public const string List = "/proc/self/mounts";

    public const string OnItself = "a file system is mounted on it and would lose its files";

    public const string Unknown = "what is mounted on this system cannot be read, and a file system mounted in it would lose its files";

    // Why the folder must not be deleted with all it holds, as the end of a sentence. Null when nothing is mounted there.
    public static string? Holding(string directory, string list = List)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        if (Real(directory) is not { } real || Lines(list) is not { } lines)
        {
            return Unknown;
        }

        return In(lines, real) switch
        {
            [] => null,
            [var only] when only == real => OnItself,
            var mounted => $"{string.Join(", ", mounted)} is still mounted in it and would lose its files",
        };
    }

    // The places, the folder's own among them. Empty on a system that mounts nothing into folders this way, and null
    // where the kernel's list cannot be read.
    public static IReadOnlyList<string>? Of(string directory, string list = List)
    {
        if (!OperatingSystem.IsLinux())
        {
            return [];
        }

        return Real(directory) is { } real && Lines(list) is { } lines ? In(lines, real) : null;
    }

    // lines are the kernel's, which names a place by where it really is. So directory must hold no link.
    public static IReadOnlyList<string> In(IEnumerable<string> lines, string directory)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentException.ThrowIfNullOrEmpty(directory);

        string itself = directory.Length > 1 ? directory.TrimEnd('/') : directory;
        string below = itself.TrimEnd('/') + "/";

        return [.. lines
            .Select(line => line.Split(' '))
            .Where(fields => fields.Length >= 2)
            .Select(fields => MountList.Unescape(fields[1]))
            .Where(place => place == itself || place.StartsWith(below, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)];
    }

    private static string[]? Lines(string list)
    {
        try
        {
            return File.ReadAllLines(list);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The folder's path with every link in it followed. Null when there is no such folder.
    private static string? Real(string directory)
    {
        byte[] resolved = new byte[LinuxFileNativeMethods.MaxPath];

        if (LinuxFileNativeMethods.realpath(directory, ref resolved[0]) == 0)
        {
            return null;
        }

        return Encoding.UTF8.GetString(resolved, 0, Math.Max(0, Array.IndexOf(resolved, (byte)0)));
    }
}

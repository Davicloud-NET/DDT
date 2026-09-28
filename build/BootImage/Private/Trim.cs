// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

// Removes files from a mounted Windows PE by a trim list. C# 5, which Windows PowerShell 5.1 compiles.
public static class DdtBootImageTrim
{
    private const uint Delete = 0x00010000;
    private const uint ShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;
    private const int FileDispositionInfoEx = 21;
    private const uint DispositionFlags = 0x1 | 0x2 | 0x10; // delete, POSIX semantics, ignore the read-only attribute
    private const uint DirectoryAttribute = 0x10;
    private const uint ReparsePointAttribute = 0x400;
    private const int ErrorNoMoreFiles = 18;
    private const int ErrorHandleEof = 38;
    private const int ErrorMoreData = 234;
    private const int ErrorDirNotEmpty = 145;
    private const int ErrorNotAllAssigned = 1300;
    private static readonly IntPtr s_invalidHandle = new IntPtr(-1);

    // A copy in a WinSxS component folder, the second name most files in Windows PE have.
    private static readonly Regex s_componentCopy = new Regex(
        @"^\\Windows\\WinSxS\\(amd64|x86|wow64|msil)_[^\\]+\\", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivilege
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindData
    {
        public uint Attributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint SizeHigh, SizeLow, Reserved0, Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AlternateName;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr token, bool disableAll, ref TokenPrivilege state, uint length, out TokenPrivilege previous, out uint returned);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindFirstFileExW(string name, int infoLevel, out FindData data, int searchOp, IntPtr filter, int flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool FindNextFileW(IntPtr find, out FindData data);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindFirstFileNameW(string name, uint flags, ref uint length, StringBuilder linkName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool FindNextFileNameW(IntPtr find, ref uint length, StringBuilder linkName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindClose(IntPtr find);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref uint info, uint length);

    // Removes what the lines name from the Windows PE mounted at mount and returns how many files and folders went.
    public static int[] Trim(string mount, string[] lines)
    {
        Regex removed;
        Regex kept;
        ReadList(lines, out removed, out kept);

        string root = Path.GetFullPath(mount).TrimEnd('\\');
        // Names of a file come back relative to its volume's root, without the drive.
        string volumeRelative = root.Substring(Path.GetPathRoot(root).Length - 1);

        List<string> files = new List<string>();
        List<string> folders = new List<string>();

        // Windows PE's files belong to TrustedInstaller, so they are opened for backup, which an administrator's backup
        // and restore privileges allow.
        long[] previous = Enable("SeBackupPrivilege", "SeRestorePrivilege");
        try
        {
            List(root, "", files, folders);

            HashSet<string> doomed = NamesToRemove(root, volumeRelative, files, removed, kept);
            foreach (string file in doomed)
            {
                Remove(root + file);
            }

            int folderCount = RemoveFolders(root, folders, removed, kept);

            return new int[] { doomed.Count, folderCount };
        }
        finally
        {
            Restore(previous);
        }
    }

    // A line is a path in the image; * stands for any characters within one name, and a path takes everything below
    // it. A line starting with ! keeps what it matches.
    private static void ReadList(string[] lines, out Regex removed, out Regex kept)
    {
        List<string> removes = new List<string>();
        List<string> keeps = new List<string>();
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
            {
                continue;
            }

            bool keep = line.StartsWith("!");
            string path = keep ? line.Substring(1) : line;
            if (!path.StartsWith("\\") || path.Contains("/"))
            {
                throw new ArgumentException(
                    "'" + line + "' is not a path in the image from its root, such as \\Windows\\Fonts\\sylfaen.ttf.");
            }

            (keep ? keeps : removes).Add(Regex.Escape(path.TrimEnd('\\')).Replace(@"\*", @"[^\\]*"));
        }

        removed = Pattern(removes);
        kept = Pattern(keeps);
    }

    // A file whose other names are all removed or copies in a WinSxS component folder goes with all its names; one
    // that has a name anywhere else keeps it and loses only the others.
    private static HashSet<string> NamesToRemove(string root, string volumeRelative, List<string> files, Regex removed, Regex kept)
    {
        HashSet<string> doomed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            if (!Taken(file, removed, kept) || doomed.Contains(file))
            {
                continue;
            }

            List<string> names = Names(root, volumeRelative, file);
            bool whole = names.TrueForAll(name => name != null && (Taken(name, removed, kept) || s_componentCopy.IsMatch(name)));
            if (whole)
            {
                doomed.UnionWith(names);
            }
            else
            {
                doomed.Add(file);
            }
        }

        return doomed;
    }

    // Deepest first, so a folder's folders are gone before it is tried. One that still holds a kept file stays.
    private static int RemoveFolders(string root, List<string> folders, Regex removed, Regex kept)
    {
        folders.Sort((a, b) => b.Length.CompareTo(a.Length));
        int count = 0;
        foreach (string folder in folders)
        {
            if (Taken(folder, removed, kept) && Remove(root + folder))
            {
                count++;
            }
        }

        return count;
    }

    private static Regex Pattern(List<string> patterns)
    {
        if (patterns.Count == 0)
        {
            return null;
        }

        return new Regex(
            @"^(" + string.Join("|", patterns.ToArray()) + @")(\\.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool Taken(string path, Regex removed, Regex kept)
    {
        return removed != null && removed.IsMatch(path) && (kept == null || !kept.IsMatch(path));
    }

    // Every file and folder below the root, as paths in the image. A junction or other reparse point is listed and
    // never followed. The \\?\ prefix, because some paths in WinSxS pass 260 characters below the mount directory.
    private static void List(string root, string folder, List<string> files, List<string> folders)
    {
        FindData data;
        // FindExInfoBasic, FIND_FIRST_EX_LARGE_FETCH.
        IntPtr find = FindFirstFileExW(@"\\?\" + root + folder + @"\*", 1, out data, 0, IntPtr.Zero, 2);
        if (find == s_invalidHandle)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot list " + root + folder);
        }

        try
        {
            do
            {
                if (data.Name == "." || data.Name == "..")
                {
                    continue;
                }

                string path = folder + "\\" + data.Name;
                if ((data.Attributes & DirectoryAttribute) == 0)
                {
                    files.Add(path);
                }
                else
                {
                    folders.Add(path);
                    if ((data.Attributes & ReparsePointAttribute) == 0)
                    {
                        List(root, path, files, folders);
                    }
                }
            }
            while (FindNextFileW(find, out data));

            int error = Marshal.GetLastWin32Error();
            if (error != ErrorNoMoreFiles)
            {
                throw new Win32Exception(error, "Cannot list " + root + folder);
            }
        }
        finally
        {
            FindClose(find);
        }
    }

    // Every name of the file, as paths in the image; null for a name outside the mount, which keeps the file.
    private static List<string> Names(string root, string volumeRelative, string file)
    {
        List<string> names = new List<string>();
        StringBuilder name = new StringBuilder(1024);
        uint length = (uint)name.Capacity;
        IntPtr find = FindFirstFileNameW(@"\\?\" + root + file, 0, ref length, name);
        if (find == s_invalidHandle && Marshal.GetLastWin32Error() == ErrorMoreData)
        {
            name = new StringBuilder((int)length);
            find = FindFirstFileNameW(@"\\?\" + root + file, 0, ref length, name);
        }

        if (find == s_invalidHandle)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot list the names of " + root + file);
        }

        try
        {
            while (true)
            {
                string found = name.ToString();
                bool inMount = found.StartsWith(volumeRelative + "\\", StringComparison.OrdinalIgnoreCase);
                names.Add(inMount ? found.Substring(volumeRelative.Length) : null);

                length = (uint)name.Capacity;
                if (FindNextFileNameW(find, ref length, name))
                {
                    continue;
                }

                int error = Marshal.GetLastWin32Error();
                if (error == ErrorMoreData)
                {
                    name = new StringBuilder((int)length);
                    if (FindNextFileNameW(find, ref length, name))
                    {
                        continue;
                    }

                    error = Marshal.GetLastWin32Error();
                }

                if (error != ErrorHandleEof)
                {
                    throw new Win32Exception(error, "Cannot list the names of " + root + file);
                }

                return names;
            }
        }
        finally
        {
            FindClose(find);
        }
    }

    // Deletes one name of a file, or an empty folder; false for a folder that is not empty. Many files in Windows PE are
    // read-only, and they are deleted as they are: their owner and permissions stay for a file that keeps another name.
    private static bool Remove(string path)
    {
        using (SafeFileHandle handle = CreateFileW(
            @"\\?\" + path, Delete, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero))
        {
            if (handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open " + path + " to delete it");
            }

            uint flags = DispositionFlags;
            if (SetFileInformationByHandle(handle, FileDispositionInfoEx, ref flags, 4))
            {
                return true;
            }

            int error = Marshal.GetLastWin32Error();
            if (error == ErrorDirNotEmpty)
            {
                return false;
            }

            throw new Win32Exception(error, "Cannot delete " + path);
        }
    }

    private static long[] Enable(params string[] names)
    {
        long[] previous = new long[names.Length * 2];
        IntPtr token;
        // TOKEN_ADJUST_PRIVILEGES, TOKEN_QUERY.
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            for (int index = 0; index < names.Length; index++)
            {
                TokenPrivilege state = new TokenPrivilege();
                state.Count = 1;
                state.Attributes = 0x2;
                if (!LookupPrivilegeValue(null, names[index], out state.Luid))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                TokenPrivilege old;
                uint returned;
                if (!AdjustTokenPrivileges(token, false, ref state, 16, out old, out returned)
                    || Marshal.GetLastWin32Error() == ErrorNotAllAssigned)
                {
                    throw new InvalidOperationException(
                        "This account does not hold " + names[index] + ", which removing files from Windows PE needs.");
                }

                // Windows reports the state before only for a privilege it changed, so none means it was enabled already.
                previous[index * 2] = state.Luid;
                previous[index * 2 + 1] = old.Count == 0 ? 0x2 : old.Attributes;
            }
        }
        finally
        {
            CloseHandle(token);
        }

        return previous;
    }

    private static void Restore(long[] previous)
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out token))
        {
            return;
        }

        try
        {
            for (int index = 0; index < previous.Length; index += 2)
            {
                TokenPrivilege state = new TokenPrivilege();
                state.Count = 1;
                state.Luid = previous[index];
                state.Attributes = (uint)previous[index + 1];
                TokenPrivilege old;
                uint returned;
                AdjustTokenPrivileges(token, false, ref state, 16, out old, out returned);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }
}

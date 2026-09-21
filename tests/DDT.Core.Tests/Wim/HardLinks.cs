// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DDT.Core.Tests.Wim;

// .NET has no API that creates a hard link.
[SupportedOSPlatform("windows")]
public static partial class HardLinks
{
    public static void Create(string link, string existingFile)
    {
        if (!CreateHardLink(link, existingFile, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Cannot link {link} to {existingFile}.");
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string fileName, string existingFileName, nint securityAttributes);
}

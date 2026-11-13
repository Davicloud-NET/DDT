// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.WindowsUpdates;

// The ole32 functions the COM thread needs.
internal static partial class ComNativeMethods
{
    public const int MultiThreaded = 0x0;

    // CLSCTX_ALL.
    public const uint AnyContext = 0x17;

    public static readonly Guid DispatchId = new("00020400-0000-0000-C000-000000000046");

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved, int flags);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid classId, nint outer, uint context, in Guid interfaceId, out nint instance);
}

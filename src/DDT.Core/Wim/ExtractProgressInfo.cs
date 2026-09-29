// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Core.Wim;

// The fields DDT reads from wimlib's struct wimlib_progress_info_extract (64-bit layout).
[StructLayout(LayoutKind.Explicit)]
internal readonly struct ExtractProgressInfo
{
    [FieldOffset(40)]
    internal readonly ulong TotalBytes;

    [FieldOffset(48)]
    internal readonly ulong CompletedBytes;
}

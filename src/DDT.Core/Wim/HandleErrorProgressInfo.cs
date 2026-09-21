// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Core.Wim;

// The fields DDT reads of wimlib's struct wimlib_progress_info_handle_error (64-bit layout).
[StructLayout(LayoutKind.Explicit)]
internal readonly struct HandleErrorProgressInfo
{
    [FieldOffset(0)]
    internal readonly nint Path;

    [FieldOffset(8)]
    internal readonly int ErrorCode;
}

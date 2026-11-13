// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.WindowsPhase;

// REASON_CONTEXT with a simple reason string. Windows reads only the string's pointer of the union then.
[StructLayout(LayoutKind.Sequential)]
internal readonly struct PowerReasonContext(nint reason)
{
    // POWER_REQUEST_CONTEXT_VERSION and POWER_REQUEST_CONTEXT_SIMPLE_STRING.
    public readonly uint Version = 0;

    public readonly uint Flags = 1;

    public readonly nint SimpleReasonString = reason;
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.MachineConsole.Native;

// struct pollfd.
[StructLayout(LayoutKind.Sequential)]
internal struct PollDescriptor
{
    public int Descriptor;
    public short Events;
    public short Returned;
}

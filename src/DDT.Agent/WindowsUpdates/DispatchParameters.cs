// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DDT.Agent.WindowsUpdates;

// DISPPARAMS. The arguments go in reverse order.
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DispatchParameters
{
    public ComVariant* Arguments;
    public int* NamedArguments;
    public uint ArgumentCount;
    public uint NamedArgumentCount;
}

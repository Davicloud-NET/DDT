// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DDT.Agent.Facts;

// The machine's memory and processors through kernel32.
public sealed class SystemHardware : ISystemHardware
{
    public ulong? InstalledMemoryKilobytes() =>
        NativeMethods.GetPhysicallyInstalledSystemMemory(out ulong kilobytes) && kilobytes > 0 ? kilobytes : null;

    public ulong? UsableMemoryBytes()
    {
        NativeMethods.MemoryStatus status = new() { Length = (uint)Unsafe.SizeOf<NativeMethods.MemoryStatus>() };

        return NativeMethods.GlobalMemoryStatusEx(ref status) && status.TotalPhys > 0 ? status.TotalPhys : null;
    }

    // The first call only asks for the size. It is asked again when it grew in between, as a processor added while
    // Windows runs would make it.
    public byte[]? ProcessorCores()
    {
        uint length = 0;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            byte[]? buffer = length == 0 ? null : new byte[length];

            if (NativeMethods.GetLogicalProcessorInformationEx(NativeMethods.RelationProcessorCore, buffer, ref length))
            {
                return buffer?[..(int)length];
            }

            if (Marshal.GetLastPInvokeError() != NativeMethods.ErrorInsufficientBuffer || length == 0)
            {
                return null;
            }
        }

        return null;
    }
}

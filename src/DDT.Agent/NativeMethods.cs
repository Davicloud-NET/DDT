// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent;

// The kernel32 functions the agent uses to read the machine's identity and facts. WinPE has all of them, while it has
// neither WMI nor the TPM Base Services.
internal static partial class NativeMethods
{
    // 'RSMB': the SMBIOS structure table.
    public const uint RawSmbiosProvider = 0x52534D42;

    // 'ACPI': the ACPI tables, each under its four letter signature.
    public const uint AcpiProvider = 0x41435049;

    // LOGICAL_PROCESSOR_RELATIONSHIP's RelationProcessorCore.
    public const int RelationProcessorCore = 0;

    public const int ErrorInsufficientBuffer = 122;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetSystemFirmwareTable(uint firmwareTableProviderSignature, uint firmwareTableId, byte[]? buffer, uint bufferSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint EnumSystemFirmwareTables(uint firmwareTableProviderSignature, byte[]? buffer, uint bufferSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLogicalProcessorInformationEx(int relationshipType, byte[]? buffer, ref uint returnedLength);

    // MEMORYSTATUSEX. The caller sets Length to the struct's size.
    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}

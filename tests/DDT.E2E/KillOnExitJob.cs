// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DDT.E2E;

// A job that holds every process the tests start, and what those start. Windows closes its handle when the test
// process ends, however it ends, and closing it kills everything in it, so no host, agent or publish outlives the
// tests.
internal static partial class KillOnExitJob
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    private static readonly SafeFileHandle s_job = Create();

    // Before the process starts one of its own, which then is in the job too.
    public static void Add(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!AssignProcessToJobObject(s_job, process.SafeHandle))
        {
            throw new Win32Exception();
        }
    }

    private static SafeFileHandle Create()
    {
        SafeFileHandle job = CreateJobObject(0, null);

        if (job.IsInvalid)
        {
            throw new Win32Exception();
        }

        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION as a 64-bit process passes it: 144 bytes, with the limit flags of its
        // basic limits at offset 16.
        byte[] limits = new byte[144];
        BinaryPrimitives.WriteUInt32LittleEndian(limits.AsSpan(16), KillOnJobClose);

        if (!SetInformationJobObject(job, ExtendedLimitInformation, limits, (uint)limits.Length))
        {
            throw new Win32Exception();
        }

        return job;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateJobObject(nint attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(SafeFileHandle job, int informationClass, byte[] information, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.MachineConsole.Native;

// The C library calls of the Linux head. Values as on x86-64.
internal static partial class Libc
{
    public const int ReadOnly = 0;
    public const int ReadWrite = 2;
    public const int NoControllingTerminal = 0x100;
    public const int NonBlocking = 0x800;
    public const int CloseOnExec = 0x80000;

    public const int Interrupted = 4;
    public const int TryAgain = 11;
    public const int NoDevice = 19;

    public const short PollIn = 0x1;
    public const short PollError = 0x8;
    public const short PollHangUp = 0x10;
    public const short PollInvalid = 0x20;

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close")]
    public static partial int Close(int descriptor);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    public static unsafe partial nint Read(int descriptor, void* buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    public static unsafe partial nint Write(int descriptor, void* buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static unsafe partial int Ioctl(int descriptor, nuint request, void* argument);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static partial int Ioctl(int descriptor, nuint request, nint argument);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    public static unsafe partial int Poll(PollDescriptor* descriptors, nuint count, int milliseconds);

    [LibraryImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    public static unsafe partial int GetSocketOption(int socket, int level, int option, void* value, uint* length);

    [LibraryImport("libc", EntryPoint = "sync")]
    public static partial void Sync();

    public static int Error => Marshal.GetLastPInvokeError();

    public static string Describe(string call) => $"{call} failed: {Marshal.GetPInvokeErrorMessage(Error)} (errno {Error})";
}

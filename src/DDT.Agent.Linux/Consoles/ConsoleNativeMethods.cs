// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Consoles;

// What starting and stopping the console, its terminal and its socket need of libc.
internal static partial class ConsoleNativeMethods
{
    public const int StandardInput = 0;
    public const int Terminate = 15;

    // KDSETMODE and KD_TEXT of linux/kd.h.
    public const nuint SetMode = 0x4B3A;
    public const nint TextMode = 0;

    // VT_GETSTATE of linux/vt.h.
    public const nuint GetState = 0x5603;

    // O_RDWR, O_NOCTTY and O_CLOEXEC.
    public const int OpenTerminal = 0x2 | 0x100 | 0x80000;

    // SOL_SOCKET and SO_PEERCRED; struct ucred is pid, uid, gid.
    public const int SocketLevel = 1;
    public const int PeerCredentials = 17;

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int kill(int process, int signal);

    [LibraryImport("libc", SetLastError = true)]
    internal static partial int ioctl(int file, nuint request, nint argument);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int file);

    [LibraryImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    internal static unsafe partial int GetSocketOption(int socket, int level, int name, int* value, uint* length);
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// The share connections of mpr.dll, which Windows PE has too, with ntlanman.dll behind it for SMB.
internal static unsafe partial class NetworkNativeMethods
{
    public const uint ResourceConnected = 1;
    public const uint ResourceTypeAny = 0;
    public const uint ResourceTypeDisk = 1;
    public const uint ConnectTemporary = 0x4;

    public const int NoError = 0;
    public const int ErrorMoreData = 234;
    public const int ErrorNoMoreItems = 259;
    public const int ErrorExtendedError = 1208;

    // NETRESOURCEW.
    [StructLayout(LayoutKind.Sequential)]
    public struct NetResource
    {
        public uint Scope;
        public uint Type;
        public uint DisplayType;
        public uint Usage;
        public char* LocalName;
        public char* RemoteName;
        public char* Comment;
        public char* Provider;
    }

    // UTF-16 strings are pinned, not copied, so the password stays in a single managed string.
    [LibraryImport("mpr.dll", EntryPoint = "WNetAddConnection2W", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int WNetAddConnection2(NetResource* resource, string password, string userName, uint flags);

    [LibraryImport("mpr.dll", EntryPoint = "WNetCancelConnection2W", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int WNetCancelConnection2(string name, uint flags, [MarshalAs(UnmanagedType.Bool)] bool force);

    [LibraryImport("mpr.dll", EntryPoint = "WNetOpenEnumW")]
    public static partial int WNetOpenEnum(uint scope, uint type, uint usage, NetResource* resource, out nint enumeration);

    [LibraryImport("mpr.dll", EntryPoint = "WNetEnumResourceW")]
    public static partial int WNetEnumResource(nint enumeration, ref uint count, void* buffer, ref uint bufferSize);

    [LibraryImport("mpr.dll", EntryPoint = "WNetCloseEnum")]
    public static partial int WNetCloseEnum(nint enumeration);

    // The network provider's own error, for ERROR_EXTENDED_ERROR, kept per thread.
    [LibraryImport("mpr.dll", EntryPoint = "WNetGetLastErrorW")]
    public static partial int WNetGetLastError(out int error, char* errorText, uint errorTextSize, char* providerName, uint providerNameSize);
}

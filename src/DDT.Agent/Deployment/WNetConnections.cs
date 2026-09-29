// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Runtime.InteropServices;
using static DDT.Agent.Deployment.NetworkNativeMethods;

namespace DDT.Agent.Deployment;

// INetworkConnections through mpr.dll, for the logon session the calling thread acts for.
public sealed unsafe class WNetConnections : INetworkConnections
{
    private const int TextLength = 512;
    private const uint FirstBufferSize = 16 * 1024;

    public NetworkError? Add(string remoteName, string userName, string password)
    {
        ArgumentNullException.ThrowIfNull(remoteName);
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(password);

        int code;

        fixed (char* remote = remoteName)
        {
            NetResource resource = new() { Type = ResourceTypeDisk, RemoteName = remote };
            code = WNetAddConnection2(&resource, password, userName, ConnectTemporary);
        }

        return Error(code);
    }

    public NetworkError? Cancel(string remoteName)
    {
        ArgumentNullException.ThrowIfNull(remoteName);

        return Error(WNetCancelConnection2(remoteName, 0, force: true));
    }

    public IReadOnlyList<string> Connected()
    {
        int code = WNetOpenEnum(ResourceConnected, ResourceTypeAny, 0, null, out nint enumeration);

        if (code != NoError)
        {
            return [];
        }

        List<string> names = [];
        uint size = FirstBufferSize;
        void* buffer = NativeMemory.Alloc(size);

        try
        {
            while (true)
            {
                uint count = uint.MaxValue;
                uint available = size;
                code = WNetEnumResource(enumeration, ref count, buffer, ref available);

                if (code == ErrorMoreData)
                {
                    NativeMemory.Free(buffer);
                    buffer = null;
                    size = Math.Max(available, size * 2);
                    buffer = NativeMemory.Alloc(size);

                    continue;
                }

                if (code != NoError)
                {
                    break;
                }

                NetResource* resources = (NetResource*)buffer;

                for (uint index = 0; index < count; index++)
                {
                    if (resources[index].RemoteName is not null)
                    {
                        names.Add(new string(resources[index].RemoteName));
                    }
                }
            }
        }
        finally
        {
            NativeMemory.Free(buffer);
            _ = WNetCloseEnum(enumeration);
        }

        return names;
    }

    // Read right away, on the thread that failed, because the provider keeps its text for ERROR_EXTENDED_ERROR per
    // thread.
    private static NetworkError? Error(int code)
    {
        if (code == NoError)
        {
            return null;
        }

        if (code == ErrorExtendedError)
        {
            char* text = stackalloc char[TextLength];
            char* provider = stackalloc char[TextLength];

            if (WNetGetLastError(out int error, text, TextLength, provider, TextLength) == NoError)
            {
                return new NetworkError(error, $"{new string(provider)}: {new string(text)}");
            }
        }

        return new NetworkError(code, new Win32Exception(code).Message);
    }
}

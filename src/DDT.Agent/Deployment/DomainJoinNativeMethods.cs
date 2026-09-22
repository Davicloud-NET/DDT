// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

internal static partial class DomainJoinNativeMethods
{
    public const uint NetSetupJoinDomain = 0x1;
    public const uint NetSetupAccountCreate = 0x2;

    // UTF-16 strings are passed pinned, not copied. Returns NET_API_STATUS: 0, or a Windows or NERR error code.
    [LibraryImport("netapi32.dll", EntryPoint = "NetJoinDomain", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int NetJoinDomain(
        string? server,
        string domain,
        string? machineAccountOrganizationalUnit,
        string? account,
        string? password,
        uint joinOptions);
}

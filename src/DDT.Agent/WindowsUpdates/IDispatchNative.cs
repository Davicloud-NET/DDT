// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DDT.Agent.WindowsUpdates;

// IDispatch, the one interface the agent declares: Windows Update is reached late-bound, by member name.
[GeneratedComInterface]
[Guid("00020400-0000-0000-C000-000000000046")]
internal unsafe partial interface IDispatchNative
{
    [PreserveSig]
    int GetTypeInfoCount(out uint count);

    [PreserveSig]
    int GetTypeInfo(uint index, uint locale, out nint typeInfo);

    [PreserveSig]
    int GetIDsOfNames(Guid* iid, char** names, uint count, uint locale, int* dispatchIds);

    [PreserveSig]
    int Invoke(int dispatchId, Guid* iid, uint locale, ushort flags, DispatchParameters* parameters, ComVariant* result, ExceptionInfo* exception, uint* argumentError);
}

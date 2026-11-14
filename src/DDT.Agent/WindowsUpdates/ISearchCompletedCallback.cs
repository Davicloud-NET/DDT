// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DDT.Agent.WindowsUpdates;

// wuapi.idl. The job and the arguments are borrowed for the call.
[GeneratedComInterface]
[Guid("88aee058-d4b0-4725-a2f1-814a67ae964c")]
internal partial interface ISearchCompletedCallback
{
    [PreserveSig]
    int Invoke(nint job, nint callbackArgs);
}

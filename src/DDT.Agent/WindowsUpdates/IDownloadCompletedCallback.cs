// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DDT.Agent.WindowsUpdates;

// wuapi.idl. The job and the arguments are borrowed for the call.
[GeneratedComInterface]
[Guid("77254866-9f5b-4c8e-b9e2-c77a8530d64b")]
internal partial interface IDownloadCompletedCallback
{
    [PreserveSig]
    int Invoke(nint job, nint callbackArgs);
}

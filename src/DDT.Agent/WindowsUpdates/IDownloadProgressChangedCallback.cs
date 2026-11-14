// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DDT.Agent.WindowsUpdates;

// wuapi.idl. The job and the arguments are borrowed for the call.
[GeneratedComInterface]
[Guid("8c3f1cdd-6173-4591-aebd-a56a53ca77c1")]
internal partial interface IDownloadProgressChangedCallback
{
    [PreserveSig]
    int Invoke(nint job, nint callbackArgs);
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DDT.Agent.WindowsUpdates;

// wuapi.idl. The job and the arguments are borrowed for the call.
[GeneratedComInterface]
[Guid("e01402d5-f8da-43ba-a012-38894bd048f1")]
internal partial interface IInstallationProgressChangedCallback
{
    [PreserveSig]
    int Invoke(nint job, nint callbackArgs);
}

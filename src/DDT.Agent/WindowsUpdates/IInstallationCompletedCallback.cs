// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DDT.Agent.WindowsUpdates;

// wuapi.idl. The job and the arguments are borrowed for the call.
[GeneratedComInterface]
[Guid("45f4f6f3-d602-4f98-9a8a-3efa152ad2d3")]
internal partial interface IInstallationCompletedCallback
{
    [PreserveSig]
    int Invoke(nint job, nint callbackArgs);
}

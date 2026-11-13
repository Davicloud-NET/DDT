// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DDT.Agent.WindowsUpdates;

// The callbacks Begin* insists on: it refuses null or empty ones. The job is polled instead, so they do nothing.
[GeneratedComClass]
internal sealed partial class JobCallback :
    ISearchCompletedCallback,
    IDownloadCompletedCallback,
    IDownloadProgressChangedCallback,
    IInstallationCompletedCallback,
    IInstallationProgressChangedCallback
{
    private static readonly StrategyBasedComWrappers s_wrappers = new();

    // The object's IUnknown, for the caller to release.
    public nint Unknown() => s_wrappers.GetOrCreateComInterfaceForObject(this, CreateComInterfaceFlags.None);

    int ISearchCompletedCallback.Invoke(nint job, nint callbackArgs) => 0;

    int IDownloadCompletedCallback.Invoke(nint job, nint callbackArgs) => 0;

    int IDownloadProgressChangedCallback.Invoke(nint job, nint callbackArgs) => 0;

    int IInstallationCompletedCallback.Invoke(nint job, nint callbackArgs) => 0;

    int IInstallationProgressChangedCallback.Invoke(nint job, nint callbackArgs) => 0;
}

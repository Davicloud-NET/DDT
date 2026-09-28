// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Wim;

public interface IWimLibrary
{
    Task ApplyAsync(
        string wimPath,
        int index,
        string targetDirectory,
        IProgress<WimProgress>? progress,
        CancellationToken cancellationToken);

    Task CaptureAsync(WimCapture capture, IProgress<WimProgress>? progress, CancellationToken cancellationToken);

    Task ExportAsync(WimExport export, IProgress<WimProgress>? progress, CancellationToken cancellationToken);
}

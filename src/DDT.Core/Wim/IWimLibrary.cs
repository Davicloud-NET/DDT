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

    Task CaptureAsync(
        string sourceDirectory,
        string wimPath,
        string imageName,
        WimCompression compression,
        IProgress<WimProgress>? progress,
        CancellationToken cancellationToken);

    Task ExportAsync(
        string sourceWimPath,
        int index,
        string destinationWimPath,
        WimCompression compression,
        IProgress<WimProgress>? progress,
        CancellationToken cancellationToken);
}

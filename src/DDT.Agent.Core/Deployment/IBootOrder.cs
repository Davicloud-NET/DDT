// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The firmware's boot order, as a run changes it.
public interface IBootOrder
{
    // Makes the firmware start loaderPath on the EFI system partition esp first, under description. It's used for the
    // fallback file of a raw disk image. A failure is only a warning.
    Task PutFirstAsync(
        EspPartition esp,
        string loaderPath,
        string description,
        IReadOnlyCollection<Guid> erasedSystemPartitionIds,
        CancellationToken cancellationToken);

    // For a run that ends without finishing, puts back the firmware boot entries and order that this run changed.
    // Never throws. A failure is only a warning.
    Task RestoreAsync(CancellationToken cancellationToken);
}

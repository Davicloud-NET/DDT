// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

public interface IBcdWriter
{
    // Makes the applied Windows bootable and registers its recovery environment.
    Task WriteAsync(TargetVolumes volumes, CancellationToken cancellationToken);

    // Puts Windows Boot Manager on the new system partition first, as the last change, after the answer file: until
    // then a restart still starts the network. A failure only warns, as Windows is installed either way.
    Task PutWindowsFirstAsync(TargetVolumes volumes, CancellationToken cancellationToken);

    // Makes the firmware start loaderPath on the EFI system partition esp first, under description, as the fallback file
    // of a raw disk image. A failure is only a warning.
    Task PutFirstAsync(
        EspPartition esp,
        string loaderPath,
        string description,
        IReadOnlyCollection<Guid> erasedSystemPartitionIds,
        CancellationToken cancellationToken);

    // Puts back the firmware boot entries and order that PutWindowsFirstAsync or PutFirstAsync changed in this run, for
    // a run that ends without finishing. Never throws: a failure is only a warning.
    Task RestoreBootOrderAsync(CancellationToken cancellationToken);
}

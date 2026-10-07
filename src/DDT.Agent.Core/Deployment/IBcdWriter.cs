// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

public interface IBcdWriter
{
    // Makes the applied Windows bootable and registers its recovery environment.
    Task WriteAsync(TargetVolumes volumes, CancellationToken cancellationToken);

    // Puts Windows Boot Manager on the new system partition first. It's the last change, after the answer file, because
    // until then a restart still boots from the network. A failure only warns, because Windows is installed either way.
    Task PutWindowsFirstAsync(TargetVolumes volumes, CancellationToken cancellationToken);
}

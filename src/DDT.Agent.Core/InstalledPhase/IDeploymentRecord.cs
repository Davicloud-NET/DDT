// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.InstalledPhase;

// HKLM\SOFTWARE\Davicloud\DDT\Deployment in Windows: what deployed the machine. It replaces what an earlier run left.
public interface IDeploymentRecord
{
    Task WriteAsync(DeploymentRecordEntry entry, CancellationToken cancellationToken);
}

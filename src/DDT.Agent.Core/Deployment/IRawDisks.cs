// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

public interface IRawDisks
{
    // Opens the disk for reading and writing. Opening changes nothing on it. Throws DeploymentStepException with a
    // sentence for the operator when the disk cannot be opened.
    IRawDisk Open(LocalDisk disk);
}

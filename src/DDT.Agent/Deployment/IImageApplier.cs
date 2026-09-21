// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

public interface IImageApplier
{
    // Makes sure an apply can run, before anything is erased. Throws with a sentence for the operator.
    void Prepare();

    Task ApplyAsync(string wimPath, int index, string targetRoot, IProgress<int> percent, CancellationToken cancellationToken);
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// Makes sure an image can be applied before anything is erased for a run that applies one.
public sealed class ImageApplierCheck(IImageApplier applier) : IPreflightCheck
{
    public bool AfterDiskChoice => true;

    public void Check(IReadOnlyList<SequenceStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.OfType<ApplyImageStep>().Any())
        {
            applier.Prepare();
        }
    }
}

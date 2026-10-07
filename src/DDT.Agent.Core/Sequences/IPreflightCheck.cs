// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// A check the platform makes of a fresh run before anything is erased. It throws DeploymentStepException with what to
// do about it.
public interface IPreflightCheck
{
    // Whether the check waits until the run's disk is chosen, as one that already prepares something does.
    bool AfterDiskChoice { get; }

    // steps are the steps on every branch of the run.
    void Check(IReadOnlyList<SequenceStep> steps);
}

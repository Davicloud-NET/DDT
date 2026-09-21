// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// Reports on the caller's thread, unlike Progress<T>, so the percent never arrives after the step has ended.
internal sealed class StepPercentProgress(Guid stepId, IProgress<StepPercent> progress) : IProgress<int>
{
    public void Report(int value) => progress.Report(new StepPercent(stepId, value));
}

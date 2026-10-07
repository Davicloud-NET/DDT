// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// Maps a part's 0 to 100 onto its share of the step, between from and to. So a step made of a download and an apply
// reports one percent that never goes back.
internal sealed class ScaledProgress(IProgress<int> percent, int from, int to) : IProgress<int>
{
    public void Report(int value) => percent.Report(from + ((to - from) * Math.Clamp(value, 0, 100) / 100));
}

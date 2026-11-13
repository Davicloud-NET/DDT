// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// Reports on the caller's thread, like StepPercentProgress. Without details it drops every text.
internal sealed class StepDetailProgress(Guid stepId, IProgress<StepDetail>? details) : IProgress<string?>
{
    public static StepDetailProgress None { get; } = new(Guid.Empty, null);

    public void Report(string? value) => details?.Report(new StepDetail(stepId, value));
}

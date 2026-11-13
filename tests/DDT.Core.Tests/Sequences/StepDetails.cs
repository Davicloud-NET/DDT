// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Sequences;

namespace DDT.Core.Tests.Sequences;

public sealed class StepDetails : IProgress<StepDetail>
{
    private readonly List<StepDetail> _reports = [];

    public IReadOnlyList<StepDetail> Reports => _reports;

    public void Report(StepDetail value) => _reports.Add(value);
}

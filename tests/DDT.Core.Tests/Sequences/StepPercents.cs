// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Sequences;

namespace DDT.Core.Tests.Sequences;

public sealed class StepPercents : IProgress<StepPercent>
{
    private readonly List<StepPercent> _reports = [];

    public IReadOnlyList<StepPercent> Reports => _reports;

    public void Report(StepPercent value) => _reports.Add(value);
}

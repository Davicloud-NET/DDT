// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsFeatures;

namespace DDT.Agent.Tests;

// Answers each name with its outcome in Outcomes, or Enabled, and records the names in the order asked.
internal sealed class FakeWindowsFeatures : IWindowsFeatures
{
    public Dictionary<string, FeatureOutcome> Outcomes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Asked { get; } = [];

    public Task<FeatureOutcome> EnableAsync(string name, CancellationToken cancellationToken)
    {
        Asked.Add(name);

        return Task.FromResult(Outcomes.GetValueOrDefault(name, FeatureOutcome.Enabled));
    }
}

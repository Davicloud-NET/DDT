// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.E2E;

// The test classes of dry runs share one lab, which publishes the agent once and starts one host, and run one after the
// other: rules match every dry run's machine, as all of them report the same model on the same network.
[CollectionDefinition(Name)]
public sealed class DryRunCollection : ICollectionFixture<DryRunLab>
{
    public const string Name = "Dry runs";
}

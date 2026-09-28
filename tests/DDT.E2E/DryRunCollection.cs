// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.E2E;

// One lab for the dry run classes. It publishes the agent once and starts one host. The classes run one after the
// other, because a rule matches every dry run's machine. They all report the same model and network.
[CollectionDefinition(Name)]
public sealed class DryRunCollection : ICollectionFixture<DryRunLab>
{
    public const string Name = "Dry runs";
}

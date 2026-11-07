// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Xunit;

namespace DDT.Agent.Tests;

// For tests that signal every process the test's own process started since some moment. No other test runs
// meanwhile, or a tool another test had just started would be ended with theirs.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessesCollection
{
    public const string Name = "Processes of the test's own";
}

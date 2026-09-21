// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

public interface IToolRunner
{
    // Returns the lines the tool wrote to its standard output. A tool that exits with a code other than 0 throws
    // DeploymentStepException.
    Task<IReadOnlyList<string>> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

public interface IToolRunner
{
    // Returns the lines the tool wrote to its standard output. A tool that exits with a code other than 0 throws
    // DeploymentStepException.
    Task<IReadOnlyList<string>> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    // Returns the exit code, whatever it is, for a caller that judges it, such as a script step. The output goes to
    // the log only, however much there is. A tool still running when options.Timeout passes is killed with every
    // process it started, and DeploymentStepException says so.
    Task<int> RunForExitCodeAsync(string fileName, IReadOnlyList<string> arguments, ToolRunOptions options, CancellationToken cancellationToken);
}

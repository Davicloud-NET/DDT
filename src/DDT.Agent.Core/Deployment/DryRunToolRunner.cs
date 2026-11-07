// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Logs each tool instead of running it, and answers like a tool that printed nothing and succeeded. So a dry run
// changes nothing on the computer it runs on, scripts included.
public sealed class DryRunToolRunner(AgentLog log) : IToolRunner
{
    public Task<IReadOnlyList<string>> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        log.Information($"Dry run: not run: {ToolRunner.CommandLine(fileName, arguments)}");

        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<int> RunForExitCodeAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        ToolRunOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        string where = options.WorkingDirectory is { } directory ? $" in {directory}" : string.Empty;
        string who = options.Account is { } account ? $" as {account.UserName}" : string.Empty;
        log.Information($"Dry run: not run{who}{where}, and taken as exit code 0: {ToolRunner.CommandLine(fileName, arguments)}");

        return Task.FromResult(0);
    }
}

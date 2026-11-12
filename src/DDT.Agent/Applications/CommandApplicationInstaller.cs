// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;

namespace DDT.Agent.Applications;

// Runs a Command application's command line as SYSTEM from a cmd file; cmd expands %NAME% references, nothing else.
public sealed class CommandApplicationInstaller(IToolRunner tools, AgentLog log) : IApplicationKindInstaller
{
    public ApplicationKind Kind => ApplicationKind.Command;

    public ApplicationOutcome Judge(AgentApplication application, int exitCode) => ApplicationInstaller.ByExitCodes(application, exitCode);

    public async Task<int> InstallAsync(ApplicationLaunch launch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);

        AgentApplication application = launch.Application;

        if (string.IsNullOrWhiteSpace(application.Command))
        {
            throw new DeploymentStepException("It has no command line. Give it one in the library, then assign the sequence again.");
        }

        string file = Path.Combine(launch.Scripts, application.Id.ToString("D") + ".cmd");
        await File.WriteAllBytesAsync(file, WindowsScriptLauncher.CmdFile(CommandPercents.ForBatchFile(application.Command)), cancellationToken).ConfigureAwait(false);

        try
        {
            ToolRunOptions options = new(launch.WorkingDirectory, launch.Environment, TimeSpan.FromMinutes(application.TimeoutMinutes));

            return await tools.RunForExitCodeAsync(WindowsScriptLauncher.CmdPath, ["/d", "/c", file], options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Leftovers.Delete(file, log);
        }
    }
}

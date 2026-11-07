// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Restarts WinPE with wpeutil, because the agent's exit leaves startnet.cmd's prompt open. For a restart back into
// WinPE the network starts next whatever the boot order says. Where that cannot be set, the restart is plain. The
// disk has no loader yet, so the firmware falls through to the network.
public sealed class WindowsPERebooter(IToolRunner tools, IUefiVariables variables, AgentLog log) : IRebooter
{
    private readonly NextStart _next = new(variables, log, AgentEnvironment.WindowsPE);

    public Task RebootAsync(RestartInto into, CancellationToken cancellationToken) => RebootAsync(into, () => Task.CompletedTask, cancellationToken);

    // wpeutil takes Windows PE down at once, so the lines written until here go to the server before it runs.
    public async Task RebootAsync(RestartInto into, Func<Task> lastLines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lastLines);

        if (into == RestartInto.BootEnvironment)
        {
            _next.FromThisEntry();
        }

        await lastLines().ConfigureAwait(false);
        await tools.RunAsync(Path.Combine(Environment.SystemDirectory, "wpeutil.exe"), ["reboot"], cancellationToken).ConfigureAwait(false);
    }
}

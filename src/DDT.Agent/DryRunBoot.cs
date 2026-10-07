// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// The machine of a dry run through its boot environments. It starts the one the agent was asked for. When the agent
// there restarts into the other one, it goes on there, with the resume token the agent left in the folder that stands
// in for the machine's firmware.
public sealed class DryRunBoot(
    DryRunMachineOptions options,
    IAgentServer server,
    Func<AgentOptions, IAgentServer> connect,
    ConsoleStatus status,
    AgentLog log,
    TimeProvider timeProvider)
{
    // Under the dry run's root, so it is gone with the run like the rest of the fake machine.
    public static string FirmwareIn(string root) => Path.Combine(root, "firmware");

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        FileVendorVariables firmware = new(FirmwareIn(options.Root));
        AgentEnvironment environment = options.Agent.DryRunEnvironment ?? AgentEnvironment.WindowsPE;

        while (true)
        {
            int exitCode = await StartAsync(environment, firmware, cancellationToken).ConfigureAwait(false);

            if (exitCode != AgentExitCodes.Restarting
                || cancellationToken.IsCancellationRequested
                || ResumeCarrier.TargetIn(firmware) is not { } target
                || target == environment)
            {
                return exitCode;
            }

            log.Information($"Dry run: the machine restarts into {AgentEnvironments.Name(target)}, which here starts its agent over there.");
            environment = target;
        }
    }

    private Task<int> StartAsync(AgentEnvironment environment, FileVendorVariables firmware, CancellationToken cancellationToken)
    {
        if (environment == AgentEnvironment.LinuxBoot)
        {
            LinuxBootDryRun linux = new(options, server, status, log, timeProvider)
            {
                FreeSpace = new DriveFreeSpace(),
                MakeSparse = SparseFile.Make,
                Firmware = firmware,
            };

            return linux.RunAsync(cancellationToken);
        }

        DryRunMachine machine = new(options, server, connect, status, log, timeProvider) { Firmware = firmware };

        return machine.RunAsync(cancellationToken);
    }
}

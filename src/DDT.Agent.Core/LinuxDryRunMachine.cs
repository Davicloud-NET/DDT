// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent;

// The machine a dry run stands in for when it starts the Linux boot environment. After a hand-over it goes on in the
// installed Linux, and each restart there starts its agent over, until the run ends. connect reaches the server for
// the installed Linux, with the settings the hand-over staged.
public sealed class LinuxDryRunMachine(
    DryRunMachineOptions options,
    IAgentServer server,
    Func<AgentOptions, IAgentServer> connect,
    ConsoleStatus status,
    AgentLog log,
    TimeProvider timeProvider)
{
    // How this computer counts the free space of a path.
    public required IFreeSpace FreeSpace { get; init; }

    // How this computer keeps the file that stands in for the disk sparse, where it has to be asked.
    public Action<SafeFileHandle>? MakeSparse { get; init; }

    // What stands in for the machine's firmware variables. Without it the fake machine cannot change its boot
    // environment.
    public IVendorVariables? Firmware { get; init; }

    // Ends when the run does, and when the agent restarts into Windows PE, which is not played here.
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        LinuxPhaseDryRun installed = new(options, connect, log, timeProvider) { FreeSpace = FreeSpace };

        while (true)
        {
            bool inLinux = await installed.HoldsRunAsync().ConfigureAwait(false);
            int exitCode = inLinux
                ? await installed.RunAsync(cancellationToken).ConfigureAwait(false)
                : await Boot().RunAsync(cancellationToken).ConfigureAwait(false);

            if (exitCode != AgentExitCodes.Restarting || cancellationToken.IsCancellationRequested || RestartsElsewhere())
            {
                return exitCode;
            }

            // The boot environment restarts only to hand a run over, which the seed on the disk then holds.
            if (!inLinux && !installed.Install())
            {
                return exitCode;
            }

            log.Information(inLinux
                ? "Dry run: the machine restarts, which here starts its agent over."
                : "Dry run: the machine restarts into the installed Linux, which here starts its agent over there.");
        }
    }

    private LinuxBootDryRun Boot() => new(options, server, status, log, timeProvider)
    {
        FreeSpace = FreeSpace,
        MakeSparse = MakeSparse,
        Firmware = Firmware,
    };

    private bool RestartsElsewhere() =>
        Firmware is not null && ResumeCarrier.TargetIn(Firmware) is { } target && target != AgentEnvironment.LinuxBoot;
}

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
        DryRunNetboot netboot = options.Agent.DryRunNetboot;

        while (true)
        {
            bool holdsRun = await installed.HoldsRunAsync().ConfigureAwait(false);
            bool inLinux = holdsRun && netboot != DryRunNetboot.OnceInLinux;
            int exitCode = inLinux
                ? await installed.RunAsync(cancellationToken).ConfigureAwait(false)
                : await Boot().RunAsync(cancellationToken).ConfigureAwait(false);

            if (exitCode != AgentExitCodes.Restarting || cancellationToken.IsCancellationRequested || RestartsElsewhere())
            {
                return exitCode;
            }

            // The boot environment restarts only for a run it handed over, which the seed on the disk then holds.
            if (netboot == DryRunNetboot.Always && !inLinux)
            {
                log.Information("Dry run: the installed Linux never runs the agent's script, and the machine starts from the network again.");

                continue;
            }

            if (!holdsRun && !installed.Install())
            {
                return exitCode;
            }

            // Once is once: the fake machine's next start comes from its disk.
            netboot = holdsRun ? DryRunNetboot.Never : netboot;
            log.Information(Says(inLinux, holdsRun, netboot));
        }
    }

    private static string Says(bool inLinux, bool heldRun, DryRunNetboot netboot) => (inLinux, heldRun, netboot) switch
    {
        (true, _, _) => "Dry run: the machine restarts, which here starts its agent over.",
        (false, false, DryRunNetboot.OnceInLinux) =>
            "Dry run: the machine restarts into the installed Linux, which takes the run over, and then starts from the network once more.",
        _ => "Dry run: the machine restarts into the installed Linux, which here starts its agent over there.",
    };

    private LinuxBootDryRun Boot() => new(options, server, status, log, timeProvider)
    {
        FreeSpace = FreeSpace,
        MakeSparse = MakeSparse,
        Firmware = Firmware,
    };

    private bool RestartsElsewhere() =>
        Firmware is not null && ResumeCarrier.TargetIn(Firmware) is { } target && target != AgentEnvironment.LinuxBoot;
}

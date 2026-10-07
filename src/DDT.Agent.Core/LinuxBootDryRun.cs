// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using Microsoft.Win32.SafeHandles;

namespace DDT.Agent;

// The machine a dry run stands in for when it starts the Linux boot environment. A file stands in for its disk, tools
// and scripts only log, and nothing on this computer changes. The run ends in the boot environment.
public sealed class LinuxBootDryRun(DryRunMachineOptions options, IAgentServer server, ConsoleStatus status, AgentLog log, TimeProvider timeProvider)
{
    // Where the real boot environment keeps a due restart.
    private const string RestartNote = "in the Linux boot environment the due restart would be recorded in /run/ddt/restart-due, which the restart clears";

    // How this computer counts the free space of a path.
    public required IFreeSpace FreeSpace { get; init; }

    // How this computer keeps the file that stands in for the disk sparse, where it has to be asked.
    public Action<SafeFileHandle>? MakeSparse { get; init; }

    // What stands in for the machine's firmware variables, where its resume token crosses a restart into Windows PE.
    // Without it the fake machine cannot change its boot environment.
    public IVendorVariables? Firmware { get; init; }

    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        log.Information("Dry run: the fake machine started the Linux boot environment, which runs the sequences that write a raw disk image.");

        DryRunDisks disks = new(options.Root, log);
        DryRunRebooter rebooter = new(log, AgentEnvironment.LinuxBoot);
        AgentMachine machine = new(
            new DryRunMachineIdentityReader(options.Agent.DryRunId, options.Agent.DryRunSecureBoot),
            disks,
            new LocalRunLocator([]),
            options.AgentVersion,
            AgentEnvironment.LinuxBoot)
        {
            Switch = Firmware is null ? null : new EnvironmentSwitch(new ResumeCarrier(Firmware, AgentEnvironment.LinuxBoot, log), rebooter),
        };

        return new AgentLoop(server, machine, status, Runner(disks, rebooter), log, timeProvider).RunAsync(cancellationToken);
    }

    // The root stands in for the agent's directory, which the run's end deletes.
    private SequenceRunner Runner(DryRunDisks disks, DryRunRebooter rebooter)
    {
        FileRawDisks rawDisks = new(options.Root, log, MakeSparse);
        DryRunBcdWriter bootOrder = new(log);
        SequenceRunnerOptions runnerOptions = new(options.HeartbeatInterval, options.Root, DryRun: true);

        return new SequenceRunnerBuilder
        {
            Server = server,
            Disks = disks,
            RawDisks = rawDisks,
            FreeSpace = FreeSpace,
            PreflightChecks = [new BootEnvironmentCheck(AgentEnvironment.LinuxBoot)],
            StepRunners = new LinuxBootStepRunners
            {
                Disks = disks,
                RawDisks = rawDisks,
                Tools = new DryRunToolRunner(log),
                Scripts = new ShellScriptLauncher(dryRun: true),
                Log = log,
                TimeProvider = timeProvider,
                Options = runnerOptions,
            },
            BcdWriter = bootOrder,
            BootOrder = bootOrder,
            Rebooter = rebooter,
            RestartMarker = new FileRestartMarker(options.Root, log, dryRun: true) { DryRunNote = RestartNote },
            HandOver = new NoHandOver(),
            Log = log,
            TimeProvider = timeProvider,
            Options = runnerOptions,
            Status = status,
            AccountTools = new AccountTools(new NoShares(), new DryRunAccountLogons(log)),
        }.Build();
    }
}

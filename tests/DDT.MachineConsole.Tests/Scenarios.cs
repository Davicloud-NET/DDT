// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.Tests;

// What an agent sends at each point of a deployment, for the tests and the screenshots.
internal static class Scenarios
{
    public static readonly Guid MachineId = Guid.Parse("0193a4b2-7c1e-7000-8000-0000000000a1");
    public static readonly Guid RunId = Guid.Parse("0193a4b2-7c1e-7000-8000-0000000000f1");

    public static readonly ConsoleDisk SystemDisk = new(0, "Samsung PM9A1 NVMe 512GB", 512_110_190_592, "Nvme", 4);
    public static readonly ConsoleDisk DataDisk = new(1, "WDC WD10EZEX-08WN4A0", 1_000_204_886_016, "Sata", 1);

    public static readonly ConsoleMachine Machine = new(
        "Dell Inc.",
        "Latitude 7450",
        "7XK2Q34",
        "4c4c4544-0037-5810-8032-b7c04f4c3334",
        ["3C52826A1F0B", "3C52826A1F0C"],
        ["10.20.4.114"],
        true,
        MicrosoftUefiCas.Ca2011 | MicrosoftUefiCas.Ca2023,
        [SystemDisk, DataDisk],
        "German (Germany)");

    public static readonly ConsoleServer Server = new("https://ddt.lab.local:8443/");

    public static readonly ConsoleStep[] Steps =
    [
        Step(1, "Partition the disk", "partition", ConsolePhase.WindowsPE, ConsoleStepState.Done),
        Step(2, "Apply image", "applyImage", ConsolePhase.WindowsPE, ConsoleStepState.Running),
        Step(3, "Inject drivers", "injectDrivers", ConsolePhase.WindowsPE, ConsoleStepState.Pending),
        Step(4, "Write the answer file", "writeUnattend", ConsolePhase.WindowsPE, ConsoleStepState.Pending),
        Step(5, "Restart into Windows", "reboot", ConsolePhase.WindowsPE, ConsoleStepState.Pending),
        Step(6, "Join the domain", "joinDomain", ConsolePhase.Windows, ConsoleStepState.Pending),
        Step(7, "Run script: baseline", "runScript", ConsolePhase.Windows, ConsoleStepState.Pending),
        Step(8, "Restart", "reboot", ConsolePhase.Windows, ConsoleStepState.Pending),
    ];

    public static ConsoleState State(ConsoleStage stage) => new(
        stage,
        "1.4.0",
        false,
        Server,
        stage is ConsoleStage.Starting ? Machine with { Disks = null } : Machine,
        stage is ConsoleStage.Starting or ConsoleStage.Connecting ? null : MachineId,
        null,
        null,
        null,
        null);

    public static ConsoleState Unreachable => State(ConsoleStage.Connecting) with
    {
        Server = Server with
        {
            Problem = "the server at ddt.lab.local:8443 did not accept a connection within 10 s (tried 10.20.4.2, name lookup 0.0 s)",
            FailedStage = ConnectionStage.Connection,
            Failures = 3,
        },
    };

    public static ConsoleRun Run(ConsoleStepState[] states, int? current, int? percent, ConsoleActivity activity = ConsoleActivity.Step) =>
        new(
            RunId,
            "Windows 11 24H2 with Office",
            [.. Steps.Select((step, index) => step with { State = states[index] })],
            current is { } index ? Steps[index].Id : null,
            percent,
            activity);

    public static ConsoleState Running => State(ConsoleStage.Running) with
    {
        Run = Run(
            [ConsoleStepState.Done, ConsoleStepState.Running, .. Pending(6)],
            1,
            62),
    };

    public static ConsoleState Preparing => State(ConsoleStage.Running) with
    {
        Run = Run([.. Pending(8)], null, null, ConsoleActivity.Preparing),
    };

    public static ConsoleState Restarting => State(ConsoleStage.Restarting) with
    {
        Run = Run([.. Done(5), .. Pending(3)], null, null, ConsoleActivity.Restarting),
        Restart = new ConsoleRestart(RestartReason.HandOver, RestartTarget.InstalledSystem),
    };

    public static ConsoleState Finished => State(ConsoleStage.Finished) with
    {
        Run = Run([.. Done(8)], null, null, ConsoleActivity.Finishing),
    };

    public static ConsoleState Failed => State(ConsoleStage.Failed) with
    {
        Run = Run(
            [ConsoleStepState.Done, ConsoleStepState.Done, ConsoleStepState.Failed, .. Pending(5)],
            null,
            null,
            ConsoleActivity.Step) with
        {
            Steps =
            [
                .. Steps.Select((step, index) => step with
                {
                    State = index < 2 ? ConsoleStepState.Done : index == 2 ? ConsoleStepState.Failed : ConsoleStepState.Pending,
                    Error = index == 2 ? "dism /Add-Driver ended with exit code 2 (0x80070002): the system cannot find the file specified" : null,
                }),
            ],
        },
        Problem = new ConsoleProblem(
            "Step 3, Inject drivers, failed: dism /Add-Driver ended with exit code 2 (0x80070002): the system cannot find the file specified",
            ConsoleRemedy.RunAgain),
    };

    public static ConsoleState Stopped => State(ConsoleStage.Stopped) with
    {
        Problem = new ConsoleProblem(
            "An administrator rejected this machine. To take that back, an operator removes it on the Machines page, and it " +
            "registers as a new machine when it starts from the network again.",
            ConsoleRemedy.Restart),
    };

    public static SequenceQuestion Sequences => new(
    [
        new SequenceOption(
            Guid.Parse("0193a4b2-7c1e-7000-8000-0000000000c1"),
            "Windows 11 24H2 with Office",
            "Enterprise, Office 365, joined to lab.local",
            true,
            true,
            true,
            42_949_672_960,
            false,
            false),
        new SequenceOption(
            Guid.Parse("0193a4b2-7c1e-7000-8000-0000000000c2"),
            "Windows 11 24H2 plain",
            "For kiosks and test benches",
            false,
            true,
            false,
            21_474_836_480,
            false,
            false),
        new SequenceOption(
            Guid.Parse("0193a4b2-7c1e-7000-8000-0000000000c3"),
            "Ubuntu 24.04 lab image",
            null,
            false,
            true,
            true,
            8_589_934_592,
            false,
            true),
    ]);

    public static DiskQuestion Disks => new("Windows 11 24H2 with Office", [SystemDisk, DataDisk]);

    public static ComputerNameQuestion ComputerName(string? error = null) => new("Windows 11 24H2 with Office", 15, error);

    public static EraseQuestion Erase => new("Windows 11 24H2 with Office", SystemDisk, "ERASE");

    public static SecureBootQuestion SecureBoot => new(
        "Ubuntu 24.04 lab image",
        "noble-lab.img",
        SecureBootProblem.UntrustedCa,
        MicrosoftUefiCas.Ca2023,
        "ANYWAY");

    public static IReadOnlyList<ConsoleLogLine> Lines { get; } =
    [
        Line(0, ConsoleLogLevel.Information, "DDT agent 1.4.0 in Windows PE 10.0.26100."),
        Line(1, ConsoleLogLevel.Information, "The graphical console, DDT console 1.4.0, is connected."),
        Line(2, ConsoleLogLevel.Information, "Registered with https://ddt.lab.local:8443/ as machine 0193a4b2-7c1e-7000-8000-0000000000a1."),
        Line(14, ConsoleLogLevel.Warning, "Wrong user name or password. The keyboard layout is German (Germany)."),
        Line(31, ConsoleLogLevel.Information, "anna signed in and authorized this machine."),
        Line(40, ConsoleLogLevel.Information, "Running Windows 11 24H2 with Office on disk 0 (Samsung PM9A1 NVMe 512GB, 477 GB, Nvme, 4 partitions)."),
        Line(41, ConsoleLogLevel.Information, "Step 1, Partition the disk: diskpart created the EFI system, MSR, Windows and recovery partitions."),
        Line(55, ConsoleLogLevel.Information, "Step 2, Apply image: downloading install.wim, 5.9 GB, from the server."),
        Line(90, ConsoleLogLevel.Error, "A request for the image timed out after 30 s; the agent tries again."),
        Line(95, ConsoleLogLevel.Information, "Step 2, Apply image: applying image 3 of install.wim to W:\\ (62 %)."),
    ];

    private static ConsoleStep Step(int number, string name, string kind, ConsolePhase phase, ConsoleStepState state) =>
        new(Guid.Parse($"0193a4b2-7c1e-7000-8000-00000000b{number:000}"), name, kind, phase, state, null);

    private static ConsoleStepState[] Pending(int count) => [.. Enumerable.Repeat(ConsoleStepState.Pending, count)];

    private static ConsoleStepState[] Done(int count) => [.. Enumerable.Repeat(ConsoleStepState.Done, count)];

    private static ConsoleLogLine Line(int seconds, ConsoleLogLevel level, string text) =>
        new(new DateTimeOffset(2026, 9, 27, 8, 14, 0, TimeSpan.Zero).AddSeconds(seconds), level, text);
}

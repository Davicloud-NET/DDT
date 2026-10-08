// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// The sentences that depend on the system the agent runs in. What Windows PE and Windows say stays word for word.
public static class EnvironmentTexts
{
    public const string LinuxUsage =
        "Usage: ddt-agent [--config <agent.json>] [--server <https url>] [--root-certificate <pem file>] " +
        "[--no-update] [--dry-run [--dry-run-id <number>] [--dry-run-secure-boot]], or ddt-agent --licenses";

    public const string LinuxNoDisk =
        "No internal disk was found. The Linux boot environment may have no driver for this PC's storage controller. " +
        "The log lists every disk it saw and why it was left out.";

    public const string LinuxDidNotStart =
        "The machine keeps starting the Linux boot environment instead of the installed Linux, so the run cannot go on there. " +
        "Set its firmware to start the disk first, then run the sequence again.";

    public static string Usage(AgentEnvironment environment) => IsLinux(environment) ? LinuxUsage : AgentOptions.Usage;

    public static string NoDisk(AgentEnvironment environment) => IsLinux(environment) ? LinuxNoDisk : SequenceRunner.NoDiskMessage;

    public static string InstalledSystemDidNotStart(AgentEnvironment environment) =>
        IsLinux(environment) ? LinuxDidNotStart : SequenceRunner.WindowsDidNotStartMessage;

    // A restart the boot environment recorded and that never happened.
    public static string RestartWasDue(AgentEnvironment environment, RestartInto into) => (IsLinux(environment), into) switch
    {
        (false, RestartInto.BootEnvironment) => "The machine was to restart into Windows PE for the run but has not restarted since. Restarting it now.",
        (false, _) => "The machine was to start the installed Windows but has not restarted since. Restarting it now.",
        (true, RestartInto.BootEnvironment) => "The machine was to restart into the Linux boot environment for the run but has not restarted since. Restarting it now.",
        (true, _) => "The machine was to start the installed Linux but has not restarted since. Restarting it now.",
    };

    // A restart the boot environment issues for a run that goes on after it.
    public static string Restarts(AgentEnvironment environment, bool handingOver) => (IsLinux(environment), handingOver) switch
    {
        (false, true) => "The machine restarts into the installed Windows, where the agent goes on with the run.",
        (false, false) => "The machine restarts into Windows PE, and the run goes on after the restart.",
        (true, true) => "The machine restarts into the installed Linux, where the agent goes on with the run.",
        (true, false) => "The machine restarts into the Linux boot environment, and the run goes on after the restart.",
    };

    public static string HandingOverAgain(AgentEnvironment environment, int returns, int most) => IsLinux(environment)
        ? $"The run goes on in the installed Linux, but the machine started the Linux boot environment. Handing the run over again ({returns} of {most} times)."
        : $"The run goes on in the installed Windows, but the machine started Windows PE. Handing the run over again ({returns} of {most} times).";

    // A restart that could not be pointed back at the boot environment.
    public static string RestartsPlainly(AgentEnvironment environment) => IsLinux(environment)
        ? "The machine restarts without choosing where it starts next, so it comes back to the Linux boot environment only if " +
            "its boot order starts from the network before the disk."
        : "The machine restarts without choosing where it starts next, so it comes back to Windows PE only if its boot order " +
            "starts from the network before the disk.";

    // Who could not tell whether Secure Boot is on. Linux reads the firmware's own variable.
    public static string SecureBootTeller(AgentEnvironment environment) => IsLinux(environment) ? "this machine's firmware" : "Windows PE";

    // What putting the boot order back keeps the machine from starting.
    public static string BootOrderIsBack(AgentEnvironment environment) => IsLinux(environment)
        ? "The firmware boot order is back as it was before the deployment, so this machine does not start a disk image whose run did not finish."
        : "The firmware boot order is back as it was before the deployment, so this machine does not start Windows without its answer file.";

    public static string BootOrderStays(AgentEnvironment environment, string error) => IsLinux(environment)
        ? $"The firmware boot order could not be put back as it was before the deployment ({error}), so this machine may " +
            "start the disk image although its run did not finish. Start it from the network to deploy it again."
        : $"The firmware boot order could not be put back as it was before the deployment ({error}), so this machine may " +
            "start Windows without its answer file. Start it from the network to deploy it again.";

    // The server's certificate comes from another DDT root than the pinned one.
    public static string OtherDdtRoot(AgentEnvironment environment) => IsLinux(environment)
        ? "The server's certificate does not come from the root certificate this machine was given when it started from the " +
            "network. The DDT server that starts machines from the network hands out its own ddt-root.pem with the Linux boot " +
            "environment, so another server answers at this address, or the server got a new root since. Start the machine " +
            "from the network again."
        : "The server's certificate does not come from the root certificate this boot image trusts. Build the " +
            "boot image again with Build-BootImage.ps1 -RootCertificatePath set to the server's ddt-root.pem, which is " +
            "next to its certificate (/var/lib/ddt/certs/ddt-root.pem in the container). A server that moved to its " +
            "own root needs this once.";

    // The server's certificate does not chain to the pinned root at all.
    public static string OtherIssuer(AgentEnvironment environment, string? issuer) => IsLinux(environment)
        ? $"The server's certificate, issued by {issuer}, does not chain to the root certificate this machine was given when " +
            "it started from the network. Have the server send the intermediate certificates after its own in the certificate " +
            "file, because the agent does not download them, then start the machine from the network again."
        : $"The server's certificate, issued by {issuer}, does not chain to the root certificate this boot image " +
            "trusts. Build the boot image with Build-BootImage.ps1 -RootCertificatePath set to the root of the CA that " +
            "issued it, and have the server send the intermediate certificates after its own in the certificate file, " +
            "because the agent does not download them.";

    private static bool IsLinux(AgentEnvironment environment) => environment == AgentEnvironment.LinuxBoot;
}

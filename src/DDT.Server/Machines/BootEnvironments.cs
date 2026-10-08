// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Core.Boot;
using DDT.Server.BootImage;
using DDT.Server.Deployments;
using DDT.Server.LinuxBoot;

namespace DDT.Server.Machines;

// The boot environments this server has files for, and whether a machine can get from one into the other.
public sealed class BootEnvironments(BootImageCatalog bootImage, LinuxBootEnvironment linuxBoot)
{
    private static readonly AgentEnvironment[] s_boot = [AgentEnvironment.WindowsPE, AgentEnvironment.LinuxBoot];

    // Windows PE once a boot image is built, and the Linux boot environment when the server came with it.
    public bool Exists(AgentEnvironment environment) => environment switch
    {
        AgentEnvironment.WindowsPE => bootImage.Builds().Count > 0,
        AgentEnvironment.LinuxBoot => linuxBoot.Available,
        _ => false,
    };

    public IReadOnlyList<AgentEnvironment> Existing() => [.. s_boot.Where(Exists)];

    // The other boot environment, when the machine's agent can restart into it and netboot would start it there.
    public AgentEnvironment? ReachableFrom(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return PhaseRules.Other(machine.AgentEnvironment) is { } other && Unreachable(machine, other) is null ? other : null;
    }

    // Why the machine cannot restart into the environment, for the technician at it. Null when it can.
    public string? Unreachable(Machine machine, AgentEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (environment == machine.AgentEnvironment)
        {
            return $"This machine already started {PhaseRules.Name(environment)}.";
        }

        if (PhaseRules.Other(machine.AgentEnvironment) != environment)
        {
            return $"A machine restarts into Windows PE or the Linux boot environment, not from {PhaseRules.Name(machine.AgentEnvironment)} into {PhaseRules.Name(environment)}.";
        }

        if (machine.SequenceVersion < PhaseRules.SwitchVersion)
        {
            return "This machine's agent is too old to change the boot environment. Start the machine from the network again, so it gets the server's agent.";
        }

        if (!Exists(environment))
        {
            return Missing(environment);
        }

        return environment == AgentEnvironment.LinuxBoot && RefusesLinux(machine)
            ? "This machine is not started in the Linux boot environment: Secure Boot is on, and its firmware does not trust the certificate " +
              "authority that signs the environment's first loader. Allow it in the firmware setup, or turn Secure Boot off there."
            : null;
    }

    // Whether netboot names this machine the Windows PE boot file where it would otherwise name the Linux boot
    // environment's: its firmware is known to refuse that one, and Windows PE can start instead. Netboot believes only
    // a row someone approved, since anyone can register a row and claim any firmware.
    public bool RefusesLinux(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return machine.FirstApprovedUtc is not null
            && machine.SecureBootEnabled == true
            && MicrosoftUefiCa.Untrusted(machine.TrustedUefiCas, linuxBoot.Current.ShimSignedUnder)
            && Exists(AgentEnvironment.WindowsPE);
    }

    // What an administrator does to get the environment.
    public static string Missing(AgentEnvironment environment) => environment == AgentEnvironment.LinuxBoot
        ? $"This server has no Linux boot environment to start. A release of DDT brings it in the folder {LinuxBootEnvironment.FolderName} next to the server."
        : "This server has no boot image to start Windows PE from. An administrator builds one on the Boot image page.";
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;

namespace DDT.Agent;

// A stable identity per dry run id, so several dry runs can stand in for several machines and a
// repeated run is recognised as the same machine.
// secureBootEnabled is what the fake machine's firmware says. It trusts both of Microsoft's third-party UEFI CAs, as an
// updated PC does, unless trustedUefiCas says otherwise.
public sealed class DryRunMachineIdentityReader(
    int dryRunId,
    bool secureBootEnabled = false,
    UefiCa trustedUefiCas = UefiCa.Microsoft2011 | UefiCa.Microsoft2023) : IMachineIdentityReader
{
    // A small virtual machine on a documentation network, so conditions on facts can be tried in a dry run.
    private static readonly MachineFacts s_facts = new()
    {
        MemoryMegabytes = 8192,
        ProcessorName = "DDT dry run processor",
        ProcessorCores = 2,
        LogicalProcessors = 4,
        TpmPresent = true,
        TpmVersion = "2.0",
        SecureBootCapable = true,
        IPv4Address = "192.0.2.10",
        IPv4PrefixLength = 24,
        DefaultGateway = "192.0.2.1",
        DnsSuffix = "dryrun.test",
        DhcpServer = "192.0.2.1",
        SystemFamily = "DDT",
        BiosVersion = "DDT 1.0",
        BiosDate = "2026-01-01",
    };

    public MachineIdentity Read()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"ddt-dry-run-{dryRunId}"));

        // 02 marks a locally administered unicast MAC, which no real network card carries.
        string mac = $"02DD{Convert.ToHexString(hash, 16, 4)}";

        return new MachineIdentity(
            new Guid(hash.AsSpan(0, 16)).ToString("D"),
            mac,
            [mac],
            "DDT",
            "Dry run",
            $"DRYRUN-{dryRunId}",
            secureBootEnabled,
            trustedUefiCas,
            Facts: s_facts);
    }
}

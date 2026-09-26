// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text;
using DDT.Contracts.Images;

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
            trustedUefiCas);
    }
}

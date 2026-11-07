// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class DryRunMachineIdentityReaderTests
{
    private static readonly IEqualityComparer<MachineIdentity> s_sameMachine =
        EqualityComparer<MachineIdentity>.Create((a, b) =>
            a is not null && b is not null
            && a.SmbiosUuid == b.SmbiosUuid
            && a.PrimaryMac == b.PrimaryMac
            && a.MacAddresses.SequenceEqual(b.MacAddresses));

    [Fact]
    public void GivesTheSameIdentityForTheSameId()
    {
        Assert.Equal(new DryRunMachineIdentityReader(3).Read(), new DryRunMachineIdentityReader(3).Read(), s_sameMachine);
    }

    [Fact]
    public void GivesDifferentMachinesForDifferentIds()
    {
        MachineIdentity first = new DryRunMachineIdentityReader(1).Read();
        MachineIdentity second = new DryRunMachineIdentityReader(2).Read();

        Assert.NotEqual(first.SmbiosUuid, second.SmbiosUuid);
        Assert.NotEqual(first.PrimaryMac, second.PrimaryMac);
    }

    [Fact]
    public void UsesAUsableUuidAndALocallyAdministeredMac()
    {
        MachineIdentity identity = new DryRunMachineIdentityReader(1).Read();

        Assert.NotEqual(Guid.Empty, Guid.Parse(identity.SmbiosUuid));
        Assert.Matches("^02[0-9A-F]{10}$", identity.PrimaryMac);
    }

    // So conditions on facts can be tried without a machine, on a network no real one is on.
    [Fact]
    public void GivesFactsOfASmallVirtualMachine()
    {
        MachineFacts? facts = new DryRunMachineIdentityReader(1).Read().Facts;

        Assert.NotNull(facts);
        Assert.Equal((8192L, 2, 4, "2.0"), (facts.MemoryMegabytes, facts.ProcessorCores, facts.LogicalProcessors, facts.TpmVersion));
        Assert.StartsWith("192.0.2.", facts.IPv4Address, StringComparison.Ordinal);
    }
}

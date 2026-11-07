// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Pxe;
using DDT.Server.Deployments;
using DDT.Server.LinuxBoot;
using DDT.Server.Machines;
using Xunit;

namespace DDT.Server.Tests;

// What ProxyDHCP is told about a machine, from the list in memory alone.
public sealed class BootChoicesTests
{
    private const string Mac = "525400DD7001";
    private const string OtherMac = "525400DD7002";

    // A USB adapter or a dock, which is plugged into one machine after another.
    private const string Dock = "0050B6AA0001";

    private static readonly Guid s_uuid = Guid.Parse("8f1d2c3b-4a5e-4f60-9a7b-0c1d2e3f4a5b");
    private static readonly Guid s_otherUuid = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly ManualTimeProvider _clock = new();
    private readonly BootChoices _choices;

    public BootChoicesTests() => _choices = new BootChoices(_clock);

    private static NetbootMachine ByMac(string mac) => new(mac, ReadOnlyMemory<byte>.Empty);

    private static BootChoiceEntry Entry(params string[] macs) => new(s_uuid, macs);

    [Fact]
    public void AMachineNobodyKnowsHasNoWish()
    {
        Assert.Equal(BootWish.None, _choices.For(ByMac(Mac)));

        _choices.Replace([Entry(OtherMac) with { Run = AgentEnvironment.LinuxBoot }]);

        Assert.Equal(BootWish.None, _choices.For(ByMac(Mac)));
        Assert.Equal(BootWish.None, _choices.For(new NetbootMachine(Mac, new byte[16])));
    }

    [Theory]
    [InlineData(AgentEnvironment.LinuxBoot)]
    [InlineData(AgentEnvironment.WindowsPE)]
    public void AMachineWithARunStartsWhereItsRunStarts(AgentEnvironment environment)
    {
        _choices.Replace([Entry(Mac, OtherMac) with { Run = environment }]);

        Assert.Equal(new BootWish(environment, false), _choices.For(ByMac(Mac)));
        Assert.Equal(new BootWish(environment, false), _choices.For(ByMac(OtherMac)));
        Assert.Equal(new BootWish(environment, false), _choices.For(ByMac(Mac.ToLowerInvariant())));
    }

    // A restart the machine was asked for counts for as long as a restart may take, and not a second longer.
    [Fact]
    public void APendingRestartCountsOnlyInsideItsWindow()
    {
        DateTimeOffset asked = _clock.GetUtcNow();
        _choices.Replace([Entry(Mac) with { Next = AgentEnvironment.LinuxBoot, NextUtc = asked }]);

        Assert.Equal(AgentEnvironment.LinuxBoot, _choices.For(ByMac(Mac)).Wanted);

        _clock.Advance(DeploymentLimits.EnvironmentSwitch);
        Assert.Equal(AgentEnvironment.LinuxBoot, _choices.For(ByMac(Mac)).Wanted);

        // The list was not read again: the window closes by the clock alone.
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(_choices.For(ByMac(Mac)).Wanted);
    }

    [Fact]
    public void ARunDecidesBeforeAPendingRestart()
    {
        _choices.Replace([Entry(Mac) with { Run = AgentEnvironment.WindowsPE, Next = AgentEnvironment.LinuxBoot, NextUtc = _clock.GetUtcNow() }]);

        Assert.Equal(AgentEnvironment.WindowsPE, _choices.For(ByMac(Mac)).Wanted);
    }

    [Fact]
    public void ARestartWithoutATimeIsNone()
    {
        _choices.Replace([Entry(Mac) with { Next = AgentEnvironment.LinuxBoot }]);

        Assert.Null(_choices.For(ByMac(Mac)).Wanted);
    }

    [Fact]
    public void SaysWhenTheMachinesFirmwareRefusesTheShim()
    {
        _choices.Replace(
        [
            Entry(Mac) with { LinuxRefused = true, Approved = true },
            new BootChoiceEntry(null, [OtherMac]) { Run = AgentEnvironment.LinuxBoot, LinuxRefused = true, Approved = true },
        ]);

        Assert.Equal(new BootWish(null, true), _choices.For(ByMac(Mac)));
        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, true), _choices.For(ByMac(OtherMac)));
    }

    // Anyone on the network can register a row and claim any firmware in it. Only an approved row is believed.
    [Fact]
    public void TheRefusalOfARowNobodyEverApprovedIsNotTaken()
    {
        _choices.Replace([Entry(Mac) with { LinuxRefused = true }]);

        Assert.Equal(BootWish.None, _choices.For(ByMac(Mac)));
        Assert.Equal(BootWish.None, _choices.For(new NetbootMachine(Mac, s_uuid.ToByteArray())));
    }

    // SMBIOS writes the first three fields of a UUID with the low byte first, RFC 4122 with the high one. Firmware
    // sends option 97 in either order.
    [Fact]
    public void FindsAMachineByItsUuidInEitherByteOrder()
    {
        _choices.Replace([Entry(OtherMac) with { Run = AgentEnvironment.LinuxBoot }]);
        byte[] asSmbios = s_uuid.ToByteArray();
        byte[] asRfc = s_uuid.ToByteArray(bigEndian: true);

        Assert.NotEqual(asSmbios, asRfc);
        Assert.Equal(AgentEnvironment.LinuxBoot, _choices.For(new NetbootMachine(Mac, asSmbios)).Wanted);
        Assert.Equal(AgentEnvironment.LinuxBoot, _choices.For(new NetbootMachine(Mac, asRfc)).Wanted);
        Assert.Null(_choices.For(new NetbootMachine(Mac, asRfc.AsMemory(0, 15))).Wanted);
    }

    // One adapter used on two machines is listed by both rows. The UUID the firmware sends says which one asks.
    [Fact]
    public void AnAdapterTwoMachinesListNamesTheOneWhoseUuidAsksWithIt()
    {
        _choices.Replace(
        [
            new BootChoiceEntry(s_uuid, [Mac, Dock]) { Run = AgentEnvironment.LinuxBoot, Approved = true },
            new BootChoiceEntry(s_otherUuid, [Dock, OtherMac]) { Run = AgentEnvironment.WindowsPE, LinuxRefused = true, Approved = true },
        ]);

        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, false), _choices.For(new NetbootMachine(Dock, s_uuid.ToByteArray())));
        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, false), _choices.For(new NetbootMachine(Dock, s_uuid.ToByteArray(bigEndian: true))));
        Assert.Equal(new BootWish(AgentEnvironment.WindowsPE, true), _choices.For(new NetbootMachine(Dock, s_otherUuid.ToByteArray())));

        // The order of the rows decides nothing.
        _choices.Replace(
        [
            new BootChoiceEntry(s_otherUuid, [Dock, OtherMac]) { Run = AgentEnvironment.WindowsPE, LinuxRefused = true, Approved = true },
            new BootChoiceEntry(s_uuid, [Mac, Dock]) { Run = AgentEnvironment.LinuxBoot, Approved = true },
        ]);

        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, false), _choices.For(new NetbootMachine(Dock, s_uuid.ToByteArray())));
        Assert.Equal(new BootWish(AgentEnvironment.WindowsPE, true), _choices.For(new NetbootMachine(Dock, s_otherUuid.ToByteArray())));
    }

    // Without a UUID that settles it, the machine with a run or a restart it was asked for is the one that starts now.
    [Fact]
    public void AnAdapterTwoMachinesListGoesToTheOneWithARunOrAnOpenRestart()
    {
        DateTimeOffset asked = _clock.GetUtcNow();
        _choices.Replace(
        [
            new BootChoiceEntry(s_uuid, [Mac, Dock]) { LinuxRefused = true, Approved = true },
            new BootChoiceEntry(s_otherUuid, [Dock, OtherMac]) { Run = AgentEnvironment.LinuxBoot, Approved = true },
        ]);

        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, false), _choices.For(ByMac(Dock)));

        _choices.Replace(
        [
            new BootChoiceEntry(s_uuid, [Mac, Dock]) { Next = AgentEnvironment.WindowsPE, NextUtc = asked, Approved = true },
            new BootChoiceEntry(s_otherUuid, [Dock, OtherMac]) { LinuxRefused = true, Approved = true },
        ]);

        Assert.Equal(new BootWish(AgentEnvironment.WindowsPE, false), _choices.For(ByMac(Dock)));

        // Once the restart's time is over neither row has a wish, so nothing tells them apart.
        _clock.Advance(DeploymentLimits.EnvironmentSwitch + TimeSpan.FromSeconds(1));
        Assert.Equal(BootWish.None, _choices.For(ByMac(Dock)));
    }

    [Fact]
    public void AnAdapterTwoMachinesWithRunsListGetsTheDefaultWhenNothingTellsThemApart()
    {
        _choices.Replace(
        [
            new BootChoiceEntry(s_uuid, [Mac, Dock]) { Run = AgentEnvironment.LinuxBoot, LinuxRefused = true, Approved = true },
            new BootChoiceEntry(s_otherUuid, [Dock, OtherMac]) { Run = AgentEnvironment.WindowsPE, LinuxRefused = true, Approved = true },
        ]);

        Assert.Equal(BootWish.None, _choices.For(ByMac(Dock)));
        Assert.Equal(BootWish.None, _choices.For(new NetbootMachine(Dock, Guid.NewGuid().ToByteArray())));

        // Each machine's own adapter still names it.
        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, true), _choices.For(ByMac(Mac)));
        Assert.Equal(new BootWish(AgentEnvironment.WindowsPE, true), _choices.For(ByMac(OtherMac)));
    }

    // An anonymous registration with a fresh UUID may list any adapters. The row it makes was never approved.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARowNobodyApprovedThatListsAnotherMachinesAdaptersChangesNothingForThatMachine(bool hostileFirst)
    {
        BootChoiceEntry victim = new(s_uuid, [Mac, OtherMac]) { Run = AgentEnvironment.LinuxBoot, Approved = true };
        BootChoiceEntry hostile = new(s_otherUuid, [Mac, OtherMac]) { LinuxRefused = true };
        _choices.Replace(hostileFirst ? [hostile, victim] : [victim, hostile]);

        // With its UUID, and from firmware that sends none.
        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, false), _choices.For(new NetbootMachine(Mac, s_uuid.ToByteArray())));
        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, false), _choices.For(new NetbootMachine(OtherMac, s_uuid.ToByteArray(bigEndian: true))));
        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, false), _choices.For(ByMac(Mac)));
    }

    // The victim's own row says its firmware refuses the shim, and the made-up row must not take that away either.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARowNobodyApprovedNeitherAddsARefusalNorRemovesOne(bool hostileFirst)
    {
        BootChoiceEntry refused = new(s_uuid, [Mac]) { LinuxRefused = true, Approved = true };
        BootChoiceEntry hostile = new(s_otherUuid, [Mac, OtherMac]) { Run = AgentEnvironment.WindowsPE };
        _choices.Replace(hostileFirst ? [hostile, refused] : [refused, hostile]);

        Assert.Equal(new BootWish(null, true), _choices.For(new NetbootMachine(Mac, s_uuid.ToByteArray())));
        Assert.Equal(new BootWish(null, true), _choices.For(ByMac(Mac)));

        BootChoiceEntry trusting = new(s_uuid, [Mac]) { Approved = true };
        BootChoiceEntry claiming = new(s_otherUuid, [Mac]) { LinuxRefused = true };
        _choices.Replace(hostileFirst ? [claiming, trusting] : [trusting, claiming]);

        Assert.Equal(BootWish.None, _choices.For(new NetbootMachine(Mac, s_uuid.ToByteArray())));
        Assert.Equal(BootWish.None, _choices.For(ByMac(Mac)));
    }

    // A registration with the victim's UUID and another adapter makes the UUID shared. Its own adapter still names it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARowNobodyApprovedThatReportsAnotherMachinesUuidChangesNothingForThatMachine(bool hostileFirst)
    {
        BootChoiceEntry victim = new(s_uuid, [Mac]) { Run = AgentEnvironment.LinuxBoot, Approved = true };
        BootChoiceEntry hostile = new(s_uuid, [OtherMac]) { LinuxRefused = true };
        _choices.Replace(hostileFirst ? [hostile, victim] : [victim, hostile]);

        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, false), _choices.For(new NetbootMachine(Mac, s_uuid.ToByteArray())));
        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, false), _choices.For(ByMac(Mac)));
        Assert.Equal(BootWish.None, _choices.For(new NetbootMachine("AABBCCDDEEFF", s_uuid.ToByteArray())));
    }

    // The adapter is one machine's and the UUID another's, which only happens when an adapter moved. A wish decides.
    [Fact]
    public void AnAdapterOfOneMachineWithTheUuidOfAnotherGoesToTheOneWithAWish()
    {
        _choices.Replace(
        [
            new BootChoiceEntry(s_uuid, [Mac, Dock]) { Approved = true },
            new BootChoiceEntry(s_otherUuid, [OtherMac]) { Run = AgentEnvironment.LinuxBoot, Approved = true },
        ]);

        Assert.Equal(AgentEnvironment.LinuxBoot, _choices.For(new NetbootMachine(Dock, s_otherUuid.ToByteArray())).Wanted);

        _choices.Replace(
        [
            new BootChoiceEntry(s_uuid, [Mac, Dock]) { Run = AgentEnvironment.LinuxBoot, Approved = true },
            new BootChoiceEntry(s_otherUuid, [OtherMac]) { Run = AgentEnvironment.WindowsPE, Approved = true },
        ]);

        Assert.Equal(BootWish.None, _choices.For(new NetbootMachine(Dock, s_otherUuid.ToByteArray())));
    }

    // Some boards ship with the same UUID, or with none. Such a UUID tells no machine from another.
    [Fact]
    public void AUuidTwoMachinesShareOrThatIsEmptyNamesNeither()
    {
        _choices.Replace(
        [
            new BootChoiceEntry(s_uuid, [Mac]) { Run = AgentEnvironment.LinuxBoot },
            new BootChoiceEntry(s_uuid, [OtherMac]) { Run = AgentEnvironment.WindowsPE },
            new BootChoiceEntry(Guid.Empty, ["525400DD7003"]) { Run = AgentEnvironment.LinuxBoot },
        ]);

        Assert.Null(_choices.For(new NetbootMachine("AABBCCDDEEFF", s_uuid.ToByteArray())).Wanted);
        Assert.Null(_choices.For(new NetbootMachine("AABBCCDDEEFF", Guid.Empty.ToByteArray())).Wanted);
        Assert.Equal(AgentEnvironment.LinuxBoot, _choices.For(new NetbootMachine(Mac, s_uuid.ToByteArray())).Wanted);
    }

    // Board makers leave a few well-known values in the UUID, so a machine nobody knows reports the same one as a
    // machine the server knows. It gets nothing of that machine's row.
    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    [InlineData("03000200-0400-0500-0006-000700080009")]
    [InlineData("12345678-1234-5678-90ab-cddeefaabbcc")]
    public void AMachineNobodyKnowsWithAUuidFirmwareFillsInGetsNothingOfTheMachineThatHasIt(string placeholder)
    {
        Guid uuid = Guid.Parse(placeholder);
        _choices.Replace([new BootChoiceEntry(uuid, [Mac]) { Run = AgentEnvironment.LinuxBoot, LinuxRefused = true, Approved = true }]);

        Assert.Equal(BootWish.None, _choices.For(new NetbootMachine("AABBCCDDEEFF", uuid.ToByteArray())));
        Assert.Equal(BootWish.None, _choices.For(new NetbootMachine("AABBCCDDEEFF", uuid.ToByteArray(bigEndian: true))));

        // The machine that has it is still found by its adapter, with the UUID or without.
        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, true), _choices.For(new NetbootMachine(Mac, uuid.ToByteArray())));
        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, true), _choices.For(ByMac(Mac)));
    }

    // A time ahead of the clock is left from before the clock was set back. It must not hold a window open for longer.
    [Fact]
    public void ARestartAskedForInTheFutureCountsForNothing()
    {
        _choices.Replace([Entry(Mac) with { Next = AgentEnvironment.LinuxBoot, NextUtc = _clock.GetUtcNow() + TimeSpan.FromSeconds(1) }]);

        Assert.Null(_choices.For(ByMac(Mac)).Wanted);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(AgentEnvironment.LinuxBoot, _choices.For(ByMac(Mac)).Wanted);
    }

    [Fact]
    public void ANewListReplacesTheOldOneWhole()
    {
        _choices.Replace([Entry(Mac) with { Run = AgentEnvironment.LinuxBoot }]);
        _choices.Replace([Entry(OtherMac) with { Run = AgentEnvironment.WindowsPE }]);

        Assert.Equal(BootWish.None with { Wanted = AgentEnvironment.WindowsPE }, _choices.For(new NetbootMachine(Mac, s_uuid.ToByteArray())));
        Assert.Equal(AgentEnvironment.WindowsPE, _choices.For(ByMac(OtherMac)).Wanted);
    }

    [Fact]
    public async Task AChangeEndsTheWaitThatWasTakenBeforeIt()
    {
        Task before = _choices.NextChange();
        Assert.False(before.IsCompleted);

        _choices.Changed();
        await before.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Task after = _choices.NextChange();
        Assert.False(after.IsCompleted);
    }

    private static Machine Row(Guid id) => new()
    {
        Id = id,
        SmbiosUuid = s_uuid.ToString("D"),
        PrimaryMac = Mac,
        MacAddresses = Mac,
        LastSeenUtc = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero),
    };

    // Machines poll and report every few seconds, and each time their row is saved. None of that moves what they start.
    [Fact]
    public void AMachineThatWasSavedAsksForANewReadOnlyWhenWhatNetbootReadsOfItChanged()
    {
        Machine machine = Row(Guid.NewGuid());
        _choices.Changed(machine);
        Task waiting = _choices.NextChange();

        machine.LastSeenUtc += TimeSpan.FromSeconds(10);
        machine.LastSeenAddress = "10.0.4.20";
        machine.AgentVersion = "26.1.500";
        machine.SignedInUserName = "operator";
        _choices.Changed(machine);
        _choices.Changed(Row(machine.Id));

        Assert.False(waiting.IsCompleted);
    }

    public static TheoryData<string> WhatNetbootReads => ["run", "restart", "restart time", "adapters", "uuid", "secure boot", "trust", "approval"];

    [Theory]
    [MemberData(nameof(WhatNetbootReads))]
    public async Task AChangeToWhatNetbootReadsOfAMachineAsksForANewRead(string what)
    {
        Machine machine = Row(Guid.NewGuid());
        _choices.Changed(machine);
        Task waiting = _choices.NextChange();

        switch (what)
        {
            case "run":
                machine.ActiveDeploymentId = Guid.NewGuid();
                break;
            case "restart":
                machine.NextEnvironment = AgentEnvironment.LinuxBoot;
                break;
            case "restart time":
                machine.NextEnvironmentUtc = _clock.GetUtcNow();
                break;
            case "adapters":
                machine.MacAddresses = Mac + "," + OtherMac;
                break;
            case "uuid":
                machine.SmbiosUuid = s_otherUuid.ToString("D");
                break;
            case "secure boot":
                machine.SecureBootEnabled = true;
                break;
            case "trust":
                machine.TrustedUefiCas = UefiCa.Microsoft2011;
                break;
            default:
                machine.FirstApprovedUtc = _clock.GetUtcNow();
                break;
        }

        _choices.Changed(machine);

        await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AMachineNobodySawBeforeAndOneThatWasRemovedAskForANewRead()
    {
        Machine machine = Row(Guid.NewGuid());
        Task first = _choices.NextChange();
        _choices.Changed(machine);
        await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Task removed = _choices.NextChange();
        _choices.Removed([machine.Id]);
        await removed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Known again, it is as new as any.
        Task again = _choices.NextChange();
        _choices.Changed(machine);
        await again.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void AChangeBeforeAnyoneWaitsIsNoError()
    {
        _choices.Changed();
    }
}

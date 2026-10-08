// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Pxe;
using DDT.Server.Deployments;
using DDT.Server.LinuxBoot;
using Xunit;

namespace DDT.Server.Tests;

// What ProxyDHCP is told about a machine, from the list in memory alone.
public sealed class BootChoicesTests
{
    private const string Mac = "525400DD7001";
    private const string OtherMac = "525400DD7002";

    private static readonly Guid s_uuid = Guid.Parse("8f1d2c3b-4a5e-4f60-9a7b-0c1d2e3f4a5b");

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
        _choices.Replace([Entry(Mac) with { LinuxRefused = true }, new BootChoiceEntry(null, [OtherMac]) { Run = AgentEnvironment.LinuxBoot, LinuxRefused = true }]);

        Assert.Equal(new BootWish(null, true), _choices.For(ByMac(Mac)));
        Assert.Equal(new BootWish(AgentEnvironment.LinuxBoot, true), _choices.For(ByMac(OtherMac)));
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

    [Fact]
    public void TheAdapterThatAsksDecidesBeforeTheUuid()
    {
        Guid other = Guid.Parse("11111111-2222-3333-4444-555555555555");
        _choices.Replace(
        [
            new BootChoiceEntry(s_uuid, [Mac]) { Run = AgentEnvironment.LinuxBoot },
            new BootChoiceEntry(other, [OtherMac]) { Run = AgentEnvironment.WindowsPE },
        ]);

        Assert.Equal(AgentEnvironment.LinuxBoot, _choices.For(new NetbootMachine(Mac, other.ToByteArray())).Wanted);
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

    [Fact]
    public void AChangeBeforeAnyoneWaitsIsNoError()
    {
        _choices.Changed();
    }
}

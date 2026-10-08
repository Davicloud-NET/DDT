// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Pxe.Tests;

// The whole rule as a table: the machine's wish, the default of the settings, and which environments can be started.
public sealed class BootEnvironmentChoiceTests
{
    private const AgentEnvironment PE = AgentEnvironment.WindowsPE;
    private const AgentEnvironment Linux = AgentEnvironment.LinuxBoot;

    // A run or a pending restart names the environment. It is started when the server has it.
    [Theory]
    [InlineData(Linux, true, true, Linux)]
    [InlineData(Linux, false, true, Linux)]
    [InlineData(PE, true, true, PE)]
    [InlineData(PE, true, false, PE)]
    public void AWishIsMetWhenTheServerCanStartIt(AgentEnvironment wanted, bool windowsPE, bool linux, AgentEnvironment expected)
    {
        foreach (DefaultBootEnvironment preferred in Enum.GetValues<DefaultBootEnvironment>())
        {
            Assert.Equal(expected, BootEnvironmentChoice.Decide(new BootWish(wanted, false), preferred, windowsPE, linux));
        }
    }

    // The other environment's agent can say why the run does not start. Silence could not.
    [Theory]
    [InlineData(Linux, true, false, PE)]
    [InlineData(PE, false, true, Linux)]
    public void AWishTheServerCannotMeetStartsTheOtherEnvironment(AgentEnvironment wanted, bool windowsPE, bool linux, AgentEnvironment expected)
    {
        foreach (DefaultBootEnvironment preferred in Enum.GetValues<DefaultBootEnvironment>())
        {
            Assert.Equal(expected, BootEnvironmentChoice.Decide(new BootWish(wanted, false), preferred, windowsPE, linux));
        }
    }

    [Theory]
    [InlineData(Linux)]
    [InlineData(PE)]
    public void AWishWithNothingToStartGetsNoAnswer(AgentEnvironment wanted)
    {
        foreach (DefaultBootEnvironment preferred in Enum.GetValues<DefaultBootEnvironment>())
        {
            Assert.Null(BootEnvironmentChoice.Decide(new BootWish(wanted, false), preferred, windowsPE: false, linux: false));
        }
    }

    // Without a wish the setting decides. Auto is Windows PE once a boot image is built, and Linux until then.
    [Theory]
    [InlineData(DefaultBootEnvironment.Auto, true, true, PE)]
    [InlineData(DefaultBootEnvironment.Auto, true, false, PE)]
    [InlineData(DefaultBootEnvironment.Auto, false, true, Linux)]
    [InlineData(DefaultBootEnvironment.Auto, false, false, PE)]
    [InlineData(DefaultBootEnvironment.WindowsPE, true, true, PE)]
    [InlineData(DefaultBootEnvironment.WindowsPE, true, false, PE)]
    [InlineData(DefaultBootEnvironment.WindowsPE, false, true, PE)]
    [InlineData(DefaultBootEnvironment.WindowsPE, false, false, PE)]
    [InlineData(DefaultBootEnvironment.Linux, true, true, Linux)]
    [InlineData(DefaultBootEnvironment.Linux, true, false, PE)]
    [InlineData(DefaultBootEnvironment.Linux, false, true, Linux)]
    [InlineData(DefaultBootEnvironment.Linux, false, false, PE)]
    public void WithoutAWishTheDefaultDecides(DefaultBootEnvironment preferred, bool windowsPE, bool linux, AgentEnvironment expected)
    {
        Assert.Equal(expected, BootEnvironmentChoice.Decide(BootWish.None, preferred, windowsPE, linux));
    }

    // Firmware that trusts none of the shim's certificate authorities with Secure Boot on would refuse the shim.
    [Theory]
    [InlineData(DefaultBootEnvironment.Auto, true, PE)]
    [InlineData(DefaultBootEnvironment.Auto, false, PE)]
    [InlineData(DefaultBootEnvironment.WindowsPE, true, PE)]
    [InlineData(DefaultBootEnvironment.WindowsPE, false, PE)]
    [InlineData(DefaultBootEnvironment.Linux, true, PE)]
    [InlineData(DefaultBootEnvironment.Linux, false, PE)]
    public void AMachineWhoseFirmwareRefusesTheShimIsNeverNamedIt(DefaultBootEnvironment preferred, bool windowsPE, AgentEnvironment expected)
    {
        Assert.Equal(expected, BootEnvironmentChoice.Decide(new BootWish(null, LinuxRefused: true), preferred, windowsPE, linux: true));
    }

    [Theory]
    [InlineData(true, PE)]
    [InlineData(false, null)]
    public void ALinuxRunOnAMachineWhoseFirmwareRefusesTheShimStartsWindowsPEOrNothing(bool windowsPE, AgentEnvironment? expected)
    {
        foreach (DefaultBootEnvironment preferred in Enum.GetValues<DefaultBootEnvironment>())
        {
            Assert.Equal(expected, BootEnvironmentChoice.Decide(new BootWish(Linux, LinuxRefused: true), preferred, windowsPE, linux: true));
        }
    }

    [Fact]
    public void AWindowsRunOnAMachineWhoseFirmwareRefusesTheShimGetsNoAnswerWithoutABootImage()
    {
        Assert.Equal(PE, BootEnvironmentChoice.Decide(new BootWish(PE, LinuxRefused: true), DefaultBootEnvironment.Auto, windowsPE: true, linux: true));
        Assert.Null(BootEnvironmentChoice.Decide(new BootWish(PE, LinuxRefused: true), DefaultBootEnvironment.Auto, windowsPE: false, linux: true));
    }

    // A run in the installed system names no boot environment, so the default applies as for no wish.
    [Fact]
    public void AWishForAnythingButABootEnvironmentCountsAsNone()
    {
        Assert.Equal(PE, BootEnvironmentChoice.Decide(new BootWish(AgentEnvironment.Windows, false), DefaultBootEnvironment.Auto, true, true));
        Assert.Equal(Linux, BootEnvironmentChoice.Decide(new BootWish(AgentEnvironment.Windows, false), DefaultBootEnvironment.Linux, true, true));
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

// The variable that carries a resume token into the other boot environment. Its name, GUID and bytes never change.
public sealed class ResumeCarrierTests : IDisposable
{
    private const string Token = "CfDJ8Kx-resume_token.0123456789";

    private readonly FakeVendorVariables _firmware = new();
    private readonly StringWriter _console = new();

    public void Dispose() => _console.Dispose();

    private ResumeCarrier Carrier(AgentEnvironment environment) =>
        new(_firmware, environment, new AgentLog(new ImmediateTimeProvider(), _console));

    // What the console showed, without each line's time.
    private string[] Lines() =>
        [.. _console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[9..])];

    [Fact]
    public void TheVariableIsDdtResumeUnderDdtsOwnGuid()
    {
        Assert.Equal("DdtResume", ResumeCarrier.VariableName);
        Assert.Equal(Guid.Parse("d34b9a64-3d20-47de-94c2-e70dfc2ecce0"), ResumeCarrier.Vendor);
    }

    // A version byte, a byte for where the machine restarts into, then the token as ASCII.
    [Theory]
    [InlineData(AgentEnvironment.WindowsPE, 1)]
    [InlineData(AgentEnvironment.LinuxBoot, 2)]
    public void WritesTheVersionTheTargetAndTheTokenAsAscii(AgentEnvironment target, byte code)
    {
        Assert.True(Carrier(AgentEnvironment.WindowsPE).Put(target, "AbC-123_x.y"));

        Assert.Equal([1, code, 0x41, 0x62, 0x43, 0x2D, 0x31, 0x32, 0x33, 0x5F, 0x78, 0x2E, 0x79], _firmware.Resume);
        Assert.Equal(["write DdtResume"], _firmware.Calls);
        Assert.Empty(Lines());
    }

    [Fact]
    public void TakesTheTokenOutAgainAndDeletesItBeforeItIsUsed()
    {
        Carrier(AgentEnvironment.WindowsPE).Put(AgentEnvironment.LinuxBoot, Token);
        _firmware.Calls.Clear();
        ResumeCarrier next = Carrier(AgentEnvironment.LinuxBoot);

        Assert.Equal(Token, next.Take());

        Assert.Null(_firmware.Resume);
        Assert.Equal(["read DdtResume", "delete DdtResume"], _firmware.Calls);
        Assert.False(next.SwitchFailed);
        Assert.Null(next.Take());
        Assert.Empty(Lines());
    }

    [Fact]
    public void AMachineThatDidNotRestartForDdtHasNothingToTake()
    {
        ResumeCarrier carrier = Carrier(AgentEnvironment.WindowsPE);

        Assert.Null(carrier.Take());

        Assert.Equal(["read DdtResume"], _firmware.Calls);
        Assert.False(carrier.SwitchFailed);
        Assert.Empty(Lines());
    }

    // The machine restarts all the same. It only costs a second sign-in.
    [Fact]
    public void AFirmwareThatDoesNotKeepTheTokenOnlyGetsAWarning()
    {
        _firmware.WriteFailure = "The firmware variable DdtResume cannot be written (Windows error 1314).";

        Assert.False(Carrier(AgentEnvironment.WindowsPE).Put(AgentEnvironment.LinuxBoot, Token));

        Assert.Null(_firmware.Resume);
        Assert.Equal(
            [
                "WARN  The firmware did not keep what lets this machine stay signed in across the restart (The firmware variable DdtResume cannot be " +
                "written (Windows error 1314).). The machine registers anew after the restart, and someone signs in at it again.",
            ],
            Lines());
    }

    [Fact]
    public void AFirmwareThatCannotBeReadOnlyGetsAWarning()
    {
        _firmware.ReadFailure = "This machine did not start in UEFI mode, so its firmware variables cannot be read.";
        ResumeCarrier carrier = Carrier(AgentEnvironment.LinuxBoot);

        Assert.Null(carrier.Take());

        Assert.False(carrier.SwitchFailed);
        Assert.StartsWith("WARN  The firmware cannot be asked whether this machine restarted for DDT (", Assert.Single(Lines()), StringComparison.Ordinal);
    }

    // The token still resumes. The next start would find the variable again, and says so.
    [Fact]
    public void AVariableThatCannotBeDeletedStillGivesItsToken()
    {
        Carrier(AgentEnvironment.WindowsPE).Put(AgentEnvironment.LinuxBoot, Token);
        _firmware.DeleteFailure = "The firmware variable DdtResume cannot be deleted (Windows error 5).";

        Assert.Equal(Token, Carrier(AgentEnvironment.LinuxBoot).Take());
        Assert.StartsWith("WARN  The firmware variable DdtResume could not be deleted (", Assert.Single(Lines()), StringComparison.Ordinal);
    }

    // The loop guard: the token was left for the other boot environment, and this one started again.
    [Theory]
    [InlineData(AgentEnvironment.WindowsPE, AgentEnvironment.LinuxBoot, "the Linux boot environment", "Windows PE")]
    [InlineData(AgentEnvironment.LinuxBoot, AgentEnvironment.WindowsPE, "Windows PE", "the Linux boot environment")]
    public void ATokenLeftForTheOtherEnvironmentMeansThatOneDidNotStart(AgentEnvironment started, AgentEnvironment target, string wanted, string got)
    {
        Carrier(started).Put(target, Token);
        ResumeCarrier again = Carrier(started);

        Assert.Equal(Token, again.Take());

        Assert.True(again.SwitchFailed);
        Assert.Null(_firmware.Resume);
        Assert.Equal(
            [
                $"WARN  This machine restarted to start {wanted}, and {got} started again: the server could not start the other boot environment. " +
                "The machine is not restarted for it again until it was started anew.",
            ],
            Lines());
    }

    // Another version's bytes, a target that is no boot environment, a token that is not ASCII, or nothing but a header.
    [Theory]
    [InlineData(new byte[] { 2, 1, 0x41 })]
    [InlineData(new byte[] { 1, 0, 0x41 })]
    [InlineData(new byte[] { 1, 3, 0x41 })]
    [InlineData(new byte[] { 1, 2, 0xC3, 0xA4 })]
    [InlineData(new byte[] { 1, 2 })]
    [InlineData(new byte[] { })]
    public void WhatThisVersionDidNotWriteIsDeletedAndNotUsed(byte[] payload)
    {
        _firmware.Resume = payload;
        ResumeCarrier carrier = Carrier(AgentEnvironment.LinuxBoot);

        Assert.Null(carrier.Take());

        Assert.Null(_firmware.Resume);
        Assert.False(carrier.SwitchFailed);
        Assert.Null(ResumeCarrier.Decode(payload));
        Assert.Equal(
            ["WARN  The firmware variable DdtResume holds nothing this agent can read, so it was deleted. The machine registers anew."],
            Lines());
    }

    // Nothing restarts into the installed Windows, and a token that is no ASCII is none of the server's.
    [Theory]
    [InlineData(AgentEnvironment.Windows, Token)]
    [InlineData(AgentEnvironment.LinuxBoot, "")]
    [InlineData(AgentEnvironment.LinuxBoot, "tökén")]
    public void RefusesWhatCannotCross(AgentEnvironment target, string token)
    {
        Assert.Null(ResumeCarrier.Encode(target, token));
        Assert.False(Carrier(AgentEnvironment.WindowsPE).Put(target, token));

        Assert.Empty(_firmware.Calls);
        Assert.Equal(
            ["WARN  The server gave no resume token that can go into the firmware. The machine registers anew after the restart, and someone signs in at it again."],
            Lines());
    }

    [Fact]
    public void ReadsBackWhatItWrote()
    {
        byte[] payload = Assert.IsType<byte[]>(ResumeCarrier.Encode(AgentEnvironment.LinuxBoot, Token));

        Assert.Equal((AgentEnvironment.LinuxBoot, Token), ResumeCarrier.Decode(payload));
        Assert.Equal(2 + Encoding.ASCII.GetByteCount(Token), payload.Length);
    }

    // A dry run asks where its machine restarts into without taking the token from the agent that starts next.
    [Fact]
    public void SaysWhereTheMachineRestartsIntoWithoutTakingTheToken()
    {
        Assert.Null(ResumeCarrier.TargetIn(_firmware));

        Carrier(AgentEnvironment.WindowsPE).Put(AgentEnvironment.LinuxBoot, Token);

        Assert.Equal(AgentEnvironment.LinuxBoot, ResumeCarrier.TargetIn(_firmware));
        Assert.NotNull(_firmware.Resume);

        _firmware.ReadFailure = "The firmware variable DdtResume cannot be read.";

        Assert.Null(ResumeCarrier.TargetIn(_firmware));
    }
}

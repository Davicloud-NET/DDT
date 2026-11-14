// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Autopilot;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class PowerShellHardwareHashReaderTests
{
    private const string PowerShell = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";

    private static readonly string s_hash = Convert.ToBase64String(new byte[3000]);

    private readonly RecordingToolRunner _tools = new();

    private PowerShellHardwareHashReader Reader => new(_tools, PowerShell);

    // The runner hands the lines to the caller's PrivateOutput, never to the log.
    private void Answer(int exitCode, params string[] lines) =>
        _tools.AnswerExitCode = (_, _, options) =>
        {
            PrivateOutput output = Assert.IsType<PrivateOutput>(options.Private);

            foreach (string line in lines)
            {
                if (exitCode == 0)
                {
                    output.Add(line);
                }
                else
                {
                    output.AddError(line);
                }
            }

            return exitCode;
        };

    [Fact]
    public async Task ReadsTheSerialNumberAndHashOfAClientInOneCall()
    {
        Answer(0, "product:WinNT", "serial:9999-9999-9999-9999-9999-9999-99 ", $"hash:{s_hash}");

        HardwareHashReading reading = await Reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new HardwareHashReading("WinNT", "9999-9999-9999-9999-9999-9999-99", s_hash), reading);
        Assert.Equal(4000, reading.Hash.Length);
        Assert.Single(_tools.Calls);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command"], PowerShellHardwareHashReader.Arguments().Take(5));
    }

    [Fact]
    public async Task AServerSaysSoWithoutAHash()
    {
        Answer(0, "product:ServerNT");

        HardwareHashReading reading = await Reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(reading.IsClient);
        Assert.Equal(string.Empty, reading.Hash);
    }

    [Theory]
    [InlineData("hash:")]
    [InlineData("hash:not base64!")]
    public async Task NoUsableHashFails(string hashLine)
    {
        Answer(0, "product:WinNT", "serial:SN-1", hashLine);

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(() => Reader.ReadAsync(TestContext.Current.CancellationToken));

        Assert.StartsWith("Windows reported no hardware hash.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedCallFailsWithPowerShellsFirstErrorLine()
    {
        Answer(1, "", "Get-CimInstance : Invalid class", "At line:1 char:1");

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(() => Reader.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal("PowerShell could not read the hardware hash (exit code 1): Get-CimInstance : Invalid class", exception.Message);
    }
}

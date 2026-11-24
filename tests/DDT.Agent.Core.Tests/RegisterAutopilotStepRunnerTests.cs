// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Autopilot;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class RegisterAutopilotStepRunnerTests
{
    // The server takes the hash only from a step it knows is running.
    [Fact]
    public async Task ReportsTheStepRunningThenHandsTheHashOver()
    {
        using AutopilotRunFixture run = new();
        AgentHardwareHash? sent = null;
        run.Server.OnRunRecord(record => sent = Assert.IsType<AgentHardwareHash>(record));

        StepResult result = await run.RunAsync(AutopilotRunFixture.Step with { GroupTag = " Sales " });

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal([$"run-report {DeploymentState.Running} session", "run-record AgentHardwareHash session"], run.Server.Calls);
        Assert.Equal(new AgentHardwareHash("SN-1", run.Reader.Reading.Hash, "Sales"), sent);
        Assert.Equal(1, run.Reader.Reads);
        Assert.Equal([100], run.Progress.Values);
    }

    [Fact]
    public async Task NeverLogsTheHash()
    {
        using AutopilotRunFixture run = new();
        run.Server.OnRunRecord(_ => { });

        await run.RunAsync();

        List<AgentLogLine> lines = await run.SentLinesAsync();
        Assert.Equal("Handed the hardware hash of serial number SN-1 to the server.", Assert.Single(lines).Message);
        Assert.DoesNotContain(lines, line => line.Message.Contains(run.Reader.Reading.Hash[..64], StringComparison.Ordinal));
    }

    [Fact]
    public async Task AServerEditionFailsWithoutHandingAnythingOver()
    {
        using AutopilotRunFixture run = new();
        run.Reader.Reading = new HardwareHashReading("ServerNT", string.Empty, string.Empty);

        StepResult result = await run.RunAsync();

        Assert.Equal(StepResult.Failed(RegisterAutopilotStepRunner.NotClient("ServerNT")), result);
        Assert.DoesNotContain(run.Server.Calls, call => call.StartsWith("run-record", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WaitsUntilAutopilotImportedTheHash()
    {
        using AutopilotRunFixture run = new();
        run.Server.OnRunRecord(_ => { })
            .OnRunHardwareHashStatus(() => new AgentHardwareHashStatus(HardwareHashUploadState.Pending, null))
            .OnRunHardwareHashStatus(() => new AgentHardwareHashStatus(HardwareHashUploadState.Imported, null));

        StepResult result = await run.RunAsync(AutopilotRunFixture.Step with { WaitForImport = true });

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(2, run.Server.Calls.Count(call => call.StartsWith("run-hardware-hash", StringComparison.Ordinal)));
        Assert.Equal([RegisterAutopilotStepRunner.PollInterval], run.Time.Delays);
        Assert.Equal([50, 100], run.Progress.Values);
    }

    [Fact]
    public async Task AFailedImportFailsTheStepWithAutopilotsWords()
    {
        using AutopilotRunFixture run = new();
        run.Server.OnRunRecord(_ => { })
            .OnRunHardwareHashStatus(() => new AgentHardwareHashStatus(HardwareHashUploadState.Failed, "ZtdDeviceAlreadyAssigned (806)"));

        StepResult result = await run.RunAsync(AutopilotRunFixture.Step with { WaitForImport = true });

        Assert.Equal(StepResult.Failed("Autopilot did not import the hardware hash: ZtdDeviceAlreadyAssigned (806)"), result);
    }

    [Fact]
    public async Task DoesNotWaitWhenTheServerUploadsNothing()
    {
        using AutopilotRunFixture run = new();
        run.Server.OnRunRecord(_ => { })
            .OnRunHardwareHashStatus(() => new AgentHardwareHashStatus(HardwareHashUploadState.None, null));

        StepResult result = await run.RunAsync(AutopilotRunFixture.Step with { WaitForImport = true });

        Assert.Equal(StepResult.Done(), result);
        Assert.Contains(await run.SentLinesAsync(), line => line.Message == RegisterAutopilotStepRunner.NoUpload);
    }

    // One minute is the first ask and two more, 30 s apart.
    [Fact]
    public async Task GivesUpAfterTheWaitLimit()
    {
        using AutopilotRunFixture run = new();
        run.Server.OnRunRecord(_ => { });

        for (int answer = 0; answer < 3; answer++)
        {
            run.Server.OnRunHardwareHashStatus(() => new AgentHardwareHashStatus(HardwareHashUploadState.Pending, null));
        }

        StepResult result = await run.RunAsync(AutopilotRunFixture.Step with { WaitForImport = true, WaitMinutes = 1 });

        Assert.Equal(StepResult.Failed("Autopilot had not imported the hardware hash after 1 minutes."), result);
        Assert.Equal(3, run.Server.Calls.Count(call => call.StartsWith("run-hardware-hash", StringComparison.Ordinal)));
    }
}

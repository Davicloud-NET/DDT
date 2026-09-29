// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WriteUnattendStepRunnerTests : IDisposable
{
    private static readonly WriteUnattendStep s_step = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a4"),
        Name = "Answer file",
        LocalAdministrator = true,
    };

    private readonly StepRunnerFixture _run = new([s_step]);
    private readonly TargetVolumes _volumes;

    public WriteUnattendStepRunnerTests()
    {
        _volumes = _run.Partitioned();
    }

    public void Dispose() => _run.Dispose();

    [Fact]
    public async Task ReportsTheStepAsRunningBeforeItAsksForTheAnswerFile()
    {
        _run.Server.OnRunUnattend(_ => TestImage.Unattend);

        await _run.WriteUnattend.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(["run-report Running session", $"run-unattend {s_step.Id} session"], _run.Server.Calls);
    }

    [Fact]
    public async Task WritesTheAnswerFileAndHasSetupDeleteItFirst()
    {
        string scripts = Path.Combine(_volumes.Windows, "Windows", "Setup", "Scripts");
        Directory.CreateDirectory(scripts);
        await File.WriteAllTextAsync(Path.Combine(scripts, "SetupComplete.cmd"), "echo from the image\r\n", TestContext.Current.CancellationToken);
        _run.Server.OnRunUnattend(_ => TestImage.Unattend);

        StepResult result = await _run.WriteUnattend.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(TestImage.Unattend, await File.ReadAllTextAsync(UnattendFile.PathIn(_volumes.Windows), TestContext.Current.CancellationToken));
        Assert.Equal(
            $"{UnattendFile.CleanupLine}\r\necho from the image\r\n",
            await File.ReadAllTextAsync(Path.Combine(scripts, "SetupComplete.cmd"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LogsWhatTheAnswerFileSetsButNoPassword()
    {
        _run.Server.OnRunUnattend(_ => TestImage.Unattend);

        await _run.WriteUnattend.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

        List<AgentLogLine> lines = await _run.SentLinesAsync();
        Assert.Contains(lines, line => line.Message.StartsWith("Wrote the answer file: computer name PC-042, ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Message.Contains("c2VjcmV0UGFzc3dvcmQ=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAnswerFileThatCannotBeWrittenWhollyIsDeleted()
    {
        // A directory where SetupComplete.cmd belongs makes the cleanup line fail after the answer file is written.
        Directory.CreateDirectory(Path.Combine(_volumes.Windows, "Windows", "Setup", "Scripts", "SetupComplete.cmd"));
        _run.Server.OnRunUnattend(_ => TestImage.Unattend);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _run.WriteUnattend.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken));

        Assert.False(File.Exists(UnattendFile.PathIn(_volumes.Windows)));
    }

    [Fact]
    public async Task ARefusalFailsTheStepWithTheServersReason()
    {
        _run.Server.OnRunUnattend(_ => throw new AgentRequestException("409", "The answer file is served only while its step runs.", HttpStatusCode.Conflict));

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => _run.WriteUnattend.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken));

        Assert.Equal("The answer file is served only while its step runs.", exception.Message);
        Assert.False(File.Exists(UnattendFile.PathIn(_volumes.Windows)));
    }
}

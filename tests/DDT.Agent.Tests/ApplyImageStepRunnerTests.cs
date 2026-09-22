// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ApplyImageStepRunnerTests : IDisposable
{
    private static readonly Guid s_imageId = Guid.Parse("0193a4b2-0000-7000-8000-00000000a001");

    private static readonly ApplyImageStep s_step = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a2"),
        Name = "Apply Windows",
        ImageId = s_imageId,
    };

    private readonly TestImage _image = new();
    private readonly StepRunnerFixture _run;

    public ApplyImageStepRunnerTests()
    {
        _run = new StepRunnerFixture([s_step], [new AgentRunImage(s_imageId, "Windows 11 Pro", _image.Sha256, _image.Content.Length, 3, 10_000)]);
        _run.Partitioned();
        _run.Server.ServeFile(_image.Sha256, _image.Content);
    }

    public void Dispose() => _run.Dispose();

    private string Cache => Path.Combine(_run.Tools.Volumes.Windows, "DDT", "cache");

    [Fact]
    public async Task DownloadsAppliesAndDeletesTheDownload()
    {
        bool downloadedIntoTheCache = false;
        _run.Tools.Applied = _ => downloadedIntoTheCache = File.Exists(Path.Combine(Cache, $"{_image.Sha256}.wim"));

        StepResult result = await _run.ApplyImage.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.Equal(new Dictionary<string, string> { [RunVariables.WindowsApplied] = "1" }, result.Outputs);
        Assert.Equal([$"open-file {_image.Sha256} 0 session"], _run.Server.Calls);
        Assert.Equal(["apply 3"], _run.Tools.Calls);
        Assert.True(_run.Tools.ImageWasThereToApply);
        Assert.True(downloadedIntoTheCache);
        Assert.False(Directory.Exists(Cache));
    }

    [Fact]
    public async Task TheDownloadIsTheFirstFortyPercent()
    {
        await _run.ApplyImage.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken);

        List<int> percents = _run.Progress.Values;
        Assert.Equal(0, percents[0]);
        Assert.Contains(ApplyImageStepRunner.DownloadPercent, percents);

        // The fake applier reports 50 percent of its part.
        Assert.Contains(70, percents);
        Assert.Equal(percents.Order(), percents);
    }

    [Fact]
    public async Task AFailedApplyStillDeletesTheDownload()
    {
        _run.Tools.FailAt = "apply";

        await Assert.ThrowsAsync<DeploymentStepException>(() => _run.ApplyImage.RunAsync(s_step, _run.Context(), TestContext.Current.CancellationToken));

        Assert.True(_run.Tools.ImageWasThereToApply);
        Assert.False(Directory.Exists(Cache));
    }

    [Fact]
    public async Task FailsWhenTheServerSentNoImageForTheStep()
    {
        ApplyImageStep other = s_step with { ImageId = Guid.Parse("0193a4b2-0000-7000-8000-00000000a0ff") };

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => _run.ApplyImage.RunAsync(other, _run.Context(), TestContext.Current.CancellationToken));

        Assert.Equal("The server sent no image for this step. Assign the sequence again.", exception.Message);
        Assert.Empty(_run.Server.Calls);
    }

    [Fact]
    public async Task RefusesAHashThatIsNoSha256()
    {
        using StepRunnerFixture run = new([s_step], [new AgentRunImage(s_imageId, "Windows 11 Pro", @"..\..\evil", 10, 1, 10)]);
        run.Partitioned();

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => run.ApplyImage.RunAsync(s_step, run.Context(), TestContext.Current.CancellationToken));

        Assert.StartsWith("The server names Windows 11 Pro by", exception.Message, StringComparison.Ordinal);
        Assert.Empty(run.Server.Calls);
    }
}

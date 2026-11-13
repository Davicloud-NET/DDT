// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.WindowsFeatures;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class EnableFeaturesStepRunnerTests
{
    private static readonly EnableFeaturesStep s_step = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000e1"),
        Name = "Enable roles and features",
        Features = ["NetFx3", "TelnetClient"],
    };

    private readonly FakeWindowsFeatures _features = new();
    private readonly StringWriter _console = new();
    private readonly RecordingProgress _progress = new();
    private readonly RecordingDetails _details = new();
    private readonly List<string> _saves = [];

    private EnableFeaturesStepRunner Runner => new(_features, new AgentLog(new ImmediateTimeProvider(), _console));

    private Task<StepResult> RunAsync(string? saved = null) =>
        Runner.RunAsync(s_step, Context(saved), TestContext.Current.CancellationToken);

    private StepContext Context(string? saved) =>
        new(
            Guid.NewGuid(),
            SequencePhase.Windows,
            new MachineVariables("Dell Inc.", "Latitude 5440", "SN-1", "4c4c4544-0042-3510-8052-b4c04f4d3232", ["00155D010203"], "PC-042", SequencePhase.Windows),
            new Dictionary<string, string>(),
            _progress)
        {
            Checkpoint = new StepCheckpoint(saved, 0, false, progress =>
            {
                _saves.Add(progress);

                return Task.CompletedTask;
            }),
            Detail = _details,
        };

    [Fact]
    public async Task EnablesEachFeatureInOrderAndSavesWhatIsDone()
    {
        StepResult result = await RunAsync();

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(["NetFx3", "TelnetClient"], _features.Asked);
        Assert.Equal(["NetFx3", "TelnetClient"], _details.Values);
        Assert.Equal(["NetFx3", "NetFx3\nTelnetClient"], _saves);
        Assert.Equal([50, 100, 100], _progress.Values);
        Assert.Contains("Features: 2 enabled.", _console.ToString(), StringComparison.Ordinal);
    }

    // The restart comes after the last feature, and the turn after it has nothing left to do.
    [Fact]
    public async Task AFeatureThatNeedsARestartRestartsTheMachineOnceAfterTheLast()
    {
        _features.Outcomes["NetFx3"] = FeatureOutcome.RestartNeeded;

        StepResult first = await RunAsync();
        StepResult after = await RunAsync(first.Progress);

        Assert.Equal(StepResult.RestartAndContinue("NetFx3\nTelnetClient"), first);
        Assert.Equal(StepResult.Done(), after);
        Assert.Equal(["NetFx3", "TelnetClient"], _features.Asked);
        Assert.Contains("Enabled NetFx3, which needs a restart.", _console.ToString(), StringComparison.Ordinal);
        Assert.Contains(EnableFeaturesStepRunner.GoesOn, _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATurnAfterTheStepWasCutOffEnablesOnlyWhatIsLeft()
    {
        StepResult result = await RunAsync("netfx3");

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(["TelnetClient"], _features.Asked);
    }

    // A feature can need the restart an earlier one asked for.
    [Fact]
    public async Task AFeatureThatFailsAfterOneThatNeedsARestartIsTriedAgainAfterTheRestart()
    {
        _features.Outcomes["NetFx3"] = FeatureOutcome.RestartNeeded;
        _features.Failing.Add("TelnetClient");

        StepResult first = await RunAsync();
        _features.Failing.Clear();
        StepResult after = await RunAsync(first.Progress);

        Assert.Equal(StepResult.RestartAndContinue("NetFx3"), first);
        Assert.Equal(StepResult.Done(), after);
        Assert.Equal(["NetFx3", "TelnetClient", "TelnetClient"], _features.Asked);
        Assert.Contains("DISM could not enable TelnetClient (exit code 0x800F0922). TelnetClient is tried again after the restart.", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeatureThatFailsWithoutARestartPendingFailsTheStep()
    {
        _features.Failing.Add("NetFx3");

        await Assert.ThrowsAsync<DeploymentStepException>(() => RunAsync());

        Assert.Equal(["NetFx3"], _features.Asked);
        Assert.Empty(_saves);
    }

    [Fact]
    public async Task AFeatureThatFailsAgainAfterTheRestartFailsTheStep()
    {
        _features.Outcomes["NetFx3"] = FeatureOutcome.RestartNeeded;
        _features.Failing.Add("TelnetClient");

        StepResult first = await RunAsync();

        await Assert.ThrowsAsync<DeploymentStepException>(() => RunAsync(first.Progress));
        Assert.Equal(["NetFx3", "TelnetClient", "TelnetClient"], _features.Asked);
    }

    [Fact]
    public async Task ANameDismDoesNotKnowFailsTheStepNamingIt()
    {
        _features.Outcomes["NetFx3"] = FeatureOutcome.Unknown;

        StepResult result = await RunAsync();

        Assert.Equal(StepResult.Failed("DISM knows no feature named NetFx3."), result);
        Assert.Equal(["NetFx3"], _features.Asked);
        Assert.Empty(_saves);
    }
}

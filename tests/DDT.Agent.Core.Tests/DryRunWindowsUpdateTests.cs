// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsUpdates;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class DryRunWindowsUpdateTests : IDisposable
{
    private readonly string _jobs = Path.Combine(Path.GetTempPath(), $"ddt-dry-run-updates-{Guid.NewGuid():N}.txt");

    public void Dispose() => File.Delete(_jobs);

    // A new instance for each turn, as after the agent starts over.
    [Fact]
    public async Task TwoTurnsInstallAndRestartAndTheThirdFindsNothing()
    {
        WindowsUpdateStep step = new() { Id = Guid.NewGuid(), Name = "Windows Update" };
        StringWriter console = new();
        List<StepResult> results = [];
        string? saved = null;

        for (int turn = 0; turn < 3; turn++)
        {
            WindowsUpdateStepRunner runner = new(() => new DryRunWindowsUpdate(new AgentLog(new ImmediateTimeProvider(), console), _jobs), new AgentLog(new ImmediateTimeProvider(), console), new ManualTimeProvider());
            StepContext context = new(Guid.NewGuid(), SequencePhase.Windows, new MachineVariables("Dell Inc.", "Latitude 5440", "SN-1", "4c4c4544-0042-3510-8052-b4c04f4d3232", ["00155D010203"], "PC-042", SequencePhase.Windows), new Dictionary<string, string>(), new RecordingProgress())
            {
                Checkpoint = new StepCheckpoint(saved, turn, false, _ => Task.CompletedTask),
            };
            StepResult result = await runner.RunAsync(step, context, TestContext.Current.CancellationToken);
            results.Add(result);
            saved = result.Progress;
        }

        Assert.Equal([StepOutcome.RestartAndContinue, StepOutcome.RestartAndContinue, StepOutcome.Done], results.Select(result => result.Outcome));
        Assert.Contains("Windows Update: 3 installed (", console.ToString(), StringComparison.Ordinal);
        Assert.Contains("Dry run: 2026-11 Cumulative Update for .NET Framework (KB5070002) is not installed", console.ToString(), StringComparison.Ordinal);
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsUpdates;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// A Windows Update step's turn against FakeWindowsUpdate, on a clock that moves only when a test moves it.
internal sealed class WindowsUpdateRun
{
    private readonly StringWriter _console = new();

    public FakeWindowsUpdate Updates { get; } = new();

    public ManualTimeProvider Time { get; } = new();

    public RecordingProgress Progress { get; } = new();

    public RecordingDetails Details { get; } = new();

    public List<string> Saves { get; } = [];

    public string Log => _console.ToString();

    public Task<StepResult> RunAsync(WindowsUpdateStep step, string? saved = null, int restarts = 0, CancellationToken? cancellationToken = null) =>
        new WindowsUpdateStepRunner(() => Updates, new AgentLog(new ImmediateTimeProvider(), _console), Time)
            .RunAsync(step, Context(saved, restarts), cancellationToken ?? TestContext.Current.CancellationToken);

    private StepContext Context(string? saved, int restarts) =>
        new(
            Guid.NewGuid(),
            SequencePhase.Windows,
            new MachineVariables("Dell Inc.", "Latitude 5440", "SN-1", "4c4c4544-0042-3510-8052-b4c04f4d3232", ["00155D010203"], "PC-042", SequencePhase.Windows),
            new Dictionary<string, string>(),
            Progress)
        {
            Checkpoint = new StepCheckpoint(saved, restarts, false, progress =>
            {
                Saves.Add(progress);

                return Task.CompletedTask;
            }),
            Detail = Details,
        };
}

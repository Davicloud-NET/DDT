// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Autopilot;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// StepRunnerFixture in the installed Windows with a Register with Autopilot step and a fake reader.
internal sealed class AutopilotRunFixture() : StepRunnerFixture([Step])
{
    public static readonly RegisterAutopilotStep Step = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000c7"),
        Name = "Register with Autopilot",
    };

    public FakeHardwareHashReader Reader { get; } = new();

    public RegisterAutopilotStepRunner Runner => new(Server, Session, ReportRunningAsync, Reader, Log, Time);

    public Task<StepResult> RunAsync(RegisterAutopilotStep? step = null) =>
        Runner.RunAsync(step ?? Step, Context(SequencePhase.Windows), TestContext.Current.CancellationToken);
}

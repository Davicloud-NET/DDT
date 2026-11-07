// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Agent.InstalledPhase;

// What tells one installed system from another for the loop that goes on with a run in it.
public sealed record InstalledPhaseProfile
{
    // What the agent registers as there.
    public required AgentEnvironment Environment { get; init; }

    public required SequencePhase Phase { get; init; }

    // The system's own setup, which comes before the run's next step.
    public required ISetupProbe Setup { get; init; }

    // What the run reports while it waits for that setup.
    public required RunActivity WaitActivity { get; init; }

    // How often the loop asks whether that setup has finished.
    public TimeSpan SetupPollInterval { get; init; } = InstalledPhaseLoop.SetupPollInterval;

    // How long an agent that is being stopped may take to send what its log still holds. Null sends nothing then.
    public TimeSpan? LastLines { get; init; }

    // Whether the system restarts once more after the agent removed itself.
    public required bool RestartsAfterRemoval { get; init; }

    public required IInstalledPhaseTexts Texts { get; init; }
}

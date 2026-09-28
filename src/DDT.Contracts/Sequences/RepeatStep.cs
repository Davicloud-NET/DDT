// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Runs Steps, then tests Until, and again until it holds, at most MaxTimes times (1 to 100): do ... until. When Until
// still does not hold after the last time, the repeat fails, unless GoOnAtLimit lets the run go on after it.
public sealed record RepeatStep : SequenceStep
{
    public IReadOnlyList<SequenceStep> Steps { get; init; } = [];

    public required ConditionNode Until { get; init; }

    public int MaxTimes { get; init; } = 3;

    public bool GoOnAtLimit { get; init; }

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => null;

    [JsonIgnore]
    public override int MinimumVersion => 3;

    [JsonIgnore]
    public override bool IsContainer => true;

    [JsonIgnore]
    public override IReadOnlyList<StepBody> Bodies => [new(StepBody.StepsName, Steps ?? [])];

    public override SequenceStep WithBodies(IReadOnlyList<IReadOnlyList<SequenceStep>> bodies)
    {
        ArgumentNullException.ThrowIfNull(bodies);

        return bodies.Count == 1 ? this with { Steps = bodies[0] } : throw new ArgumentException("A repeat has one body.", nameof(bodies));
    }
}

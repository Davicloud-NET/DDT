// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Runs its steps in order. Its conditions and ContinueOnError apply to all of them, so the whole group is skipped
// when the conditions don't hold. Shares belong to the steps inside, and each is connected while its step runs.
public sealed record GroupStep : SequenceStep
{
    public IReadOnlyList<SequenceStep> Steps { get; init; } = [];

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

        return bodies.Count == 1 ? this with { Steps = bodies[0] } : throw new ArgumentException("A group has one body.", nameof(bodies));
    }
}

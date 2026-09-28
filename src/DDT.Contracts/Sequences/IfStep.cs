// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// Runs Then when Test holds and Else when it does not; both paths meet again after it. An else-if is an IF in Else.
public sealed record IfStep : SequenceStep
{
    public required ConditionNode Test { get; init; }

    public IReadOnlyList<SequenceStep> Then { get; init; } = [];

    public IReadOnlyList<SequenceStep> Else { get; init; } = [];

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => null;

    [JsonIgnore]
    public override int MinimumVersion => 3;

    [JsonIgnore]
    public override bool IsContainer => true;

    [JsonIgnore]
    public override IReadOnlyList<StepBody> Bodies => [new(StepBody.ThenName, Then ?? []), new(StepBody.ElseName, Else ?? [])];

    public override SequenceStep WithBodies(IReadOnlyList<IReadOnlyList<SequenceStep>> bodies)
    {
        ArgumentNullException.ThrowIfNull(bodies);

        return bodies.Count == 2
            ? this with { Then = bodies[0], Else = bodies[1] }
            : throw new ArgumentException("An IF has two bodies, Then and Else.", nameof(bodies));
    }
}

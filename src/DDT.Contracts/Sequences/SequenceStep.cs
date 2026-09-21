// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// The discriminator values are stored in sequences and run snapshots, so they never change.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PartitionStep), "partition")]
[JsonDerivedType(typeof(ApplyImageStep), "applyImage")]
[JsonDerivedType(typeof(InjectDriversStep), "injectDrivers")]
[JsonDerivedType(typeof(WriteUnattendStep), "writeUnattend")]
[JsonDerivedType(typeof(JoinDomainStep), "joinDomain")]
[JsonDerivedType(typeof(RunScriptStep), "runScript")]
[JsonDerivedType(typeof(RebootStep), "reboot")]
public abstract record SequenceStep
{
    // Stable across edits: run state, reports and problems name a step by it.
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    // The step runs only when every condition holds.
    public IReadOnlyList<StepCondition> Conditions { get; init; } = [];

    public bool ContinueOnError { get; init; }

    public bool RebootAfter { get; init; }

    // Kind facts, so the engine and the validator never switch on kinds. Null runs the step in the phase of the
    // step before it. Overrides repeat [JsonIgnore]: the source generator reads it from the override.
    [JsonIgnore]
    public abstract SequencePhase? RequiredPhase { get; }

    [JsonIgnore]
    public virtual bool ErasesDisk => false;
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// The discriminator values are stored in sequences and run snapshots, so they never change. A step is a node of the
// sequence's tree. It's either a leaf or a container (group, if, repeat) whose bodies hold more nodes.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PartitionStep), "partition")]
[JsonDerivedType(typeof(ApplyImageStep), "applyImage")]
[JsonDerivedType(typeof(InjectDriversStep), "injectDrivers")]
[JsonDerivedType(typeof(WriteUnattendStep), "writeUnattend")]
[JsonDerivedType(typeof(JoinDomainStep), "joinDomain")]
[JsonDerivedType(typeof(RunScriptStep), "runScript")]
[JsonDerivedType(typeof(RebootStep), "reboot")]
[JsonDerivedType(typeof(WriteRawImageStep), "writeRawImage")]
[JsonDerivedType(typeof(WriteCloudInitSeedStep), "writeCloudInitSeed")]
[JsonDerivedType(typeof(GroupStep), "group")]
[JsonDerivedType(typeof(IfStep), "if")]
[JsonDerivedType(typeof(RepeatStep), "repeat")]
[JsonDerivedType(typeof(SetVariableStep), "setVariable")]
[JsonDerivedType(typeof(PauseStep), "pause")]
public abstract record SequenceStep
{
    // Stable across edits, because run state, reports and problems name a step by it.
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    // The step only runs when every condition holds and When holds too. Kept for version 1 and 2 documents, which
    // older agents run.
    public IReadOnlyList<StepCondition> Conditions { get; init; } = [];

    public bool ContinueOnError { get; init; }

    public bool RebootAfter { get; init; }

    // Version 3. Left out of the JSON while unset, so a document without them reads and writes as before.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ConditionNode? When { get; init; }

    // The shares DDT connects with an account before the step runs and disconnects after it. Null is none. Only a step
    // that does something has them, not a group, IF or repeat.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ShareConnection>? Shares { get; init; }

    // Facts about each kind, so the engine and the validator never switch on kinds. Null runs the step in the phase of
    // the step before it. Overrides repeat [JsonIgnore] because the source generator reads it from the override.
    [JsonIgnore]
    public abstract SequencePhase? RequiredPhase { get; }

    [JsonIgnore]
    public virtual bool ErasesDisk => false;

    // The lowest SequenceDefinition.Version whose agents can run this kind with these members.
    // SequenceTree.RequiredVersion covers what every kind has, such as When.
    [JsonIgnore]
    public virtual int MinimumVersion => 1;

    // True for group, if and repeat. These nodes hold other nodes instead of doing something themselves.
    [JsonIgnore]
    public virtual bool IsContainer => false;

    // A container's lists of nodes, in the order the tree walks them. A list that is null in a document from outside
    // is empty here.
    [JsonIgnore]
    public virtual IReadOnlyList<StepBody> Bodies => [];

    // When a run resumes, a leaf found Running runs again instead of failing as interrupted, because running it twice
    // does no harm.
    [JsonIgnore]
    public virtual bool Resumable => false;

    // Returns a copy whose bodies hold these nodes, one list per body in the order of Bodies. A leaf has no bodies and
    // is returned as is.
    public virtual SequenceStep WithBodies(IReadOnlyList<IReadOnlyList<SequenceStep>> bodies)
    {
        ArgumentNullException.ThrowIfNull(bodies);

        return bodies.Count == 0 ? this : throw new ArgumentException("A step without bodies takes none.", nameof(bodies));
    }
}

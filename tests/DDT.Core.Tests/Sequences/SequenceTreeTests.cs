// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Core.Tests.Sequences;

public sealed class SequenceTreeTests
{
    private static readonly TestCondition s_latitude = new(MachineVariableNames.Model, ConditionOperator.Contains, "Latitude");

    // Partition, then a group holding a repeat, which holds an IF (a script in Then, a variable and a restart in Else)
    // and a pause, and a restart after the repeat; a restart at the end.
    private static readonly PartitionStep s_partition = new() { Id = NodeId(1), Name = "Partition" };
    private static readonly RunScriptStep s_script = new() { Id = NodeId(5), Name = "Script", Script = "exit 0" };
    private static readonly SetVariableStep s_set = new() { Id = NodeId(6), Name = "Set", Variable = "Office" };
    private static readonly RebootStep s_elseRestart = new() { Id = NodeId(7), Name = "Restart in Else" };
    private static readonly IfStep s_if = new() { Id = NodeId(4), Name = "If", Test = s_latitude, Then = [s_script], Else = [s_set, s_elseRestart] };
    private static readonly PauseStep s_pause = new() { Id = NodeId(8), Name = "Pause" };
    private static readonly RepeatStep s_repeat = new() { Id = NodeId(3), Name = "Repeat", Until = s_latitude, Steps = [s_if, s_pause] };
    private static readonly RebootStep s_groupRestart = new() { Id = NodeId(9), Name = "Restart in the group" };
    private static readonly GroupStep s_group = new() { Id = NodeId(2), Name = "Group", Steps = [s_repeat, s_groupRestart] };
    private static readonly RebootStep s_end = new() { Id = NodeId(10), Name = "Restart at the end" };
    private static readonly SequenceDefinition s_nested = new(3, [s_partition, s_group, s_end]);

    [Fact]
    public void ListsEveryNodeInPreOrder()
    {
        Assert.Equal<SequenceStep>(
            [s_partition, s_group, s_repeat, s_if, s_script, s_set, s_elseRestart, s_pause, s_groupRestart, s_end],
            SequenceTree.Nodes(s_nested));
        Assert.Equal<SequenceStep>([s_partition, s_script, s_set, s_elseRestart, s_pause, s_groupRestart, s_end], SequenceTree.Leaves(s_nested));
    }

    [Fact]
    public void KnowsWhereEveryNodeSits()
    {
        IReadOnlyDictionary<Guid, NodePosition> index = SequenceTree.Index(s_nested);

        Assert.Equal(10, index.Count);
        Assert.Equal(new NodePosition(s_partition, null, null, 0, 0, 0), index[s_partition.Id]);
        Assert.Equal(new NodePosition(s_group, null, null, 1, 1, 0), index[s_group.Id]);
        Assert.Equal(new NodePosition(s_repeat, s_group.Id, "steps", 0, 2, 1), index[s_repeat.Id]);
        Assert.Equal(new NodePosition(s_if, s_repeat.Id, "steps", 0, 3, 2), index[s_if.Id]);
        Assert.Equal(new NodePosition(s_script, s_if.Id, "then", 0, 4, 3), index[s_script.Id]);
        Assert.Equal(new NodePosition(s_set, s_if.Id, "else", 0, 5, 3), index[s_set.Id]);
        Assert.Equal(new NodePosition(s_elseRestart, s_if.Id, "else", 1, 6, 3), index[s_elseRestart.Id]);
        Assert.Equal(new NodePosition(s_pause, s_repeat.Id, "steps", 1, 7, 2), index[s_pause.Id]);
        Assert.Equal(new NodePosition(s_groupRestart, s_group.Id, "steps", 1, 8, 1), index[s_groupRestart.Id]);
        Assert.Equal(new NodePosition(s_end, null, null, 2, 9, 0), index[s_end.Id]);
    }

    [Fact]
    public void GoesOnAfterANodeWithItsSiblingOrByLeavingItsParent()
    {
        IReadOnlyDictionary<Guid, NodePosition> index = SequenceTree.Index(s_nested);

        NodeCursor? After(SequenceStep node) => SequenceTree.Successor(s_nested, index, node.Id);

        Assert.Equal(new NodeCursor(s_group.Id, false), After(s_partition));
        Assert.Equal(new NodeCursor(s_if.Id, true), After(s_script));
        Assert.Equal(new NodeCursor(s_elseRestart.Id, false), After(s_set));
        Assert.Equal(new NodeCursor(s_if.Id, true), After(s_elseRestart));
        Assert.Equal(new NodeCursor(s_pause.Id, false), After(s_if));
        Assert.Equal(new NodeCursor(s_repeat.Id, true), After(s_pause));
        Assert.Equal(new NodeCursor(s_groupRestart.Id, false), After(s_repeat));
        Assert.Equal(new NodeCursor(s_group.Id, true), After(s_groupRestart));
        Assert.Equal(new NodeCursor(s_end.Id, false), After(s_group));
        Assert.Null(After(s_end));
        Assert.Throws<ArgumentException>(() => SequenceTree.Successor(s_nested, index, Guid.NewGuid()));
    }

    // A flat document walks as Format 1 did: one index after the other.
    [Fact]
    public void WalksAFlatDocumentAsAList()
    {
        SequenceDefinition flat = new(1, [s_partition, s_end]);
        IReadOnlyDictionary<Guid, NodePosition> index = SequenceTree.Index(flat);

        Assert.Equal<SequenceStep>([s_partition, s_end], SequenceTree.Nodes(flat));
        Assert.Equal(new NodeCursor(s_end.Id, false), SequenceTree.Successor(flat, index, s_partition.Id));
        Assert.Null(SequenceTree.Successor(flat, index, s_end.Id));
    }

    // Documents come from outside: null lists and nodes are passed over, and a repeated id keeps its first node.
    [Fact]
    public void PassesOverWhatADocumentFromOutsideLeftOut()
    {
        GroupStep empty = new() { Id = NodeId(20), Name = "Empty", Steps = null! };
        IfStep open = new() { Id = NodeId(21), Name = "Open", Test = s_latitude, Then = null!, Else = [null!, s_end] };
        RebootStep twin = s_end with { Name = "Twin" };
        SequenceDefinition definition = new(3, [null!, empty, open, twin]);

        Assert.Equal<SequenceStep>([empty, open, s_end, twin], SequenceTree.Nodes(definition));
        Assert.Equal(s_end, SequenceTree.Index(definition)[s_end.Id].Step);
        Assert.Equal(new NodePosition(s_end, open.Id, "else", 1, 2, 1), SequenceTree.Index(definition)[s_end.Id]);
        Assert.Equal(new NodeCursor(open.Id, true), SequenceTree.Successor(definition, SequenceTree.Index(definition), s_end.Id));
        Assert.Empty(SequenceTree.Nodes(new SequenceDefinition(1, null!)));
    }

    public static TheoryData<string> Features => [.. s_features.Keys];

    private static readonly Dictionary<string, (int Version, SequenceDefinition Definition)> s_features = new(StringComparer.Ordinal)
    {
        ["flat"] = (1, Flat(Restart() with { Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.Contains, "Latitude")] })),
        ["raw image"] = (2, Flat(new WriteRawImageStep { Id = NodeId(30), Name = "Raw", ImageId = NodeId(31) })),
        ["group"] = (3, Flat(new GroupStep { Id = NodeId(30), Name = "Group" })),
        ["if"] = (3, Flat(new IfStep { Id = NodeId(30), Name = "If", Test = s_latitude })),
        ["repeat"] = (3, Flat(new RepeatStep { Id = NodeId(30), Name = "Repeat", Until = s_latitude })),
        ["set variable"] = (3, Flat(new SetVariableStep { Id = NodeId(30), Name = "Set", Variable = "Office" })),
        ["pause"] = (3, Flat(new PauseStep { Id = NodeId(30), Name = "Pause" })),
        ["raw image in a group"] = (3, Flat(new GroupStep { Id = NodeId(30), Name = "Group", Steps = [new WriteRawImageStep { Id = NodeId(31), Name = "Raw", ImageId = NodeId(32) }] })),
        ["when"] = (3, Flat(Restart() with { When = s_latitude })),
        ["shares"] = (3, Flat(Restart() with { Shares = [new ShareConnection(@"\\files\drivers", new AccountReference(NodeId(40), null))] })),
        ["no shares"] = (1, Flat(Restart() with { Shares = [] })),
        ["run as"] = (3, Flat(new RunScriptStep { Id = NodeId(30), Name = "Script", Script = "exit 0", RunAs = new AccountReference(null, "Installer") })),
        ["join account"] = (3, Flat(new JoinDomainStep { Id = NodeId(30), Name = "Join", Account = new AccountReference(NodeId(40), null) })),
        ["variables"] = (3, Flat(Restart()) with { Variables = [new VariableDeclaration { Name = "Office" }] }),
        ["no variables"] = (1, Flat(Restart()) with { Variables = [], Inputs = [] }),
        ["inputs"] = (3, Flat(Restart()) with { Inputs = [new InputDeclaration { Name = "Office", Label = "Office" }] }),
        ["new operator"] = (3, Flat(Restart() with { Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.NotContains, "Latitude")] })),
        ["new variable"] = (3, Flat(Restart() with { Conditions = [new StepCondition(MachineVariableNames.FriendlyModel, ConditionOperator.Equals, "ThinkPad T14")] })),
        ["unknown variable"] = (3, Flat(Restart() with { Conditions = [new StepCondition("model", ConditionOperator.Equals, "Latitude")] })),
    };

    [Theory]
    [MemberData(nameof(Features))]
    public void NeedsVersion3ForEveryFeatureOfTheTree(string feature)
    {
        (int version, SequenceDefinition definition) = s_features[feature];

        Assert.Equal(version, definition.RequiredVersion());
    }

    // Documents come from outside, so members declared non-null can be null.
    [Fact]
    public void WorksOutTheVersionOfADocumentWithNulls()
    {
        Assert.Equal(1, new SequenceDefinition(1, null!).RequiredVersion());
        Assert.Equal(1, Flat(null!, Restart() with { Conditions = null!, Shares = null }).RequiredVersion());
        Assert.Equal(1, Flat(Restart() with { Conditions = [null!] }).RequiredVersion());
        Assert.Equal(1, Flat(Restart() with { Conditions = [new StepCondition(null!, ConditionOperator.Equals, "x")] }).RequiredVersion());
        Assert.Equal(2, Flat(null!, new WriteRawImageStep { Id = NodeId(30), Name = "Raw", ImageId = NodeId(31) }).RequiredVersion());
        Assert.Equal(3, Flat(new GroupStep { Id = NodeId(30), Name = "Group", Steps = null! }).RequiredVersion());
        Assert.Equal(3, Flat(Restart() with { When = new AllCondition { Parts = null! } }).RequiredVersion());
        Assert.Equal(1, new SequenceDefinition(1, null!).Normalised().Version);
    }

    [Fact]
    public void StoresAWhenOlderAgentsCanRunAsConditions()
    {
        StepCondition dell = new(MachineVariableNames.Manufacturer, ConditionOperator.Equals, "Dell Inc.");
        RebootStep restart = Restart() with
        {
            Conditions = [dell],
            When = new AllCondition
            {
                Parts =
                [
                    s_latitude,
                    new TestCondition(MachineVariableNames.Phase, ConditionOperator.Equals, "WindowsPE"),
                ],
            },
            Shares = [],
        };

        SequenceDefinition normalised = Flat(restart).Normalised();

        RebootStep folded = Assert.IsType<RebootStep>(Assert.Single(normalised.Steps));
        Assert.Equal(1, normalised.Version);
        Assert.Null(folded.When);
        Assert.Null(folded.Shares);
        Assert.Equal<StepCondition>(
            [dell, new StepCondition(MachineVariableNames.Model, ConditionOperator.Contains, "Latitude"), new StepCondition(MachineVariableNames.Phase, ConditionOperator.Equals, "WindowsPE")],
            folded.Conditions);

        // A lone test and an all of nothing need no tree either.
        Assert.Equal<StepCondition>([new StepCondition(MachineVariableNames.Model, ConditionOperator.Contains, "Latitude")], Folded(s_latitude).Conditions);
        Assert.Empty(Folded(new AllCondition()).Conditions);
        Assert.Null(Folded(new AllCondition()).When);
    }

    public static TheoryData<string> KeptWhens => [.. s_keptWhens.Keys];

    private static readonly Dictionary<string, ConditionNode> s_keptWhens = new(StringComparer.Ordinal)
    {
        ["new operator"] = new AllCondition { Parts = [s_latitude, new TestCondition(MachineVariableNames.Model, ConditionOperator.EndsWith, "7440")] },
        ["new variable"] = new AllCondition { Parts = [new TestCondition(MachineVariableNames.MemoryMegabytes, ConditionOperator.Equals, "8192")] },
        ["any"] = new AnyCondition { Parts = [s_latitude] },
        ["none"] = new NoneCondition { Parts = [s_latitude] },
        ["an all in an all"] = new AllCondition { Parts = [new AllCondition { Parts = [s_latitude] }] },
    };

    [Theory]
    [MemberData(nameof(KeptWhens))]
    public void KeepsAWhenOlderAgentsCannotRun(string name)
    {
        ConditionNode when = s_keptWhens[name];

        SequenceDefinition normalised = Flat(Restart() with { When = when }).Normalised();

        Assert.Equal(3, normalised.Version);
        Assert.Same(when, Assert.Single(normalised.Steps).When);
        Assert.Empty(Assert.Single(normalised.Steps).Conditions);
    }

    // Agents of versions 1 and 2 refuse more than ten conditions before a run.
    [Fact]
    public void KeepsAWhenThatWouldMakeTooManyConditions()
    {
        StepCondition condition = new(MachineVariableNames.Model, ConditionOperator.Contains, "Latitude");
        AllCondition when = new() { Parts = [s_latitude, s_latitude] };
        RebootStep nine = Restart() with { Conditions = [.. Enumerable.Repeat(condition, 9)], When = when };
        RebootStep eight = nine with { Conditions = [.. Enumerable.Repeat(condition, 8)] };

        Assert.Same(when, Assert.Single(Flat(nine).Normalised().Steps).When);
        Assert.Equal(SequenceTree.LegacyMaxConditions, Assert.Single(Flat(eight).Normalised().Steps).Conditions.Count);
    }

    // Inside a container too, although the container keeps the document at version 3.
    [Fact]
    public void FoldsAWhenAtEveryDepth()
    {
        RebootStep inner = Restart() with { When = s_latitude };
        IfStep choose = new() { Id = NodeId(30), Name = "If", Test = s_latitude, Else = [inner] };

        SequenceDefinition normalised = Flat(new GroupStep { Id = NodeId(31), Name = "Group", Steps = [choose] }).Normalised();

        IfStep folded = Assert.IsType<IfStep>(Assert.Single(Assert.IsType<GroupStep>(Assert.Single(normalised.Steps)).Steps));
        Assert.Same(s_latitude, folded.Test);
        Assert.Null(Assert.Single(folded.Else).When);
        Assert.Single(Assert.Single(folded.Else).Conditions);
        Assert.Equal(3, normalised.Version);
    }

    [Fact]
    public void LeavesADocumentWithNothingToFoldAsItWas()
    {
        SequenceDefinition definition = s_nested with { Variables = [new VariableDeclaration { Name = "Office" }] };

        SequenceDefinition normalised = definition.Normalised();

        Assert.Same(definition.Steps, normalised.Steps);
        Assert.Same(definition.Variables, normalised.Variables);
        Assert.Null(normalised.Inputs);
        Assert.Null((definition with { Variables = [] }).Normalised().Variables);
    }

    [Fact]
    public void GivesEachContainerItsBodies()
    {
        Assert.Equal<string>(["steps"], s_group.Bodies.Select(body => body.Name));
        Assert.Equal<string>(["then", "else"], s_if.Bodies.Select(body => body.Name));
        Assert.Equal<string>(["steps"], s_repeat.Bodies.Select(body => body.Name));
        Assert.Empty(s_end.Bodies);
        Assert.True(s_group.IsContainer && s_if.IsContainer && s_repeat.IsContainer);
        Assert.False(s_end.IsContainer || s_set.IsContainer || s_pause.IsContainer);
        Assert.True(s_set.Resumable && s_pause.Resumable);
        Assert.False(s_end.Resumable || s_script.Resumable || s_group.Resumable);
        Assert.Null(s_group.RequiredPhase ?? s_if.RequiredPhase ?? s_repeat.RequiredPhase ?? s_set.RequiredPhase ?? s_pause.RequiredPhase);

        IfStep swapped = Assert.IsType<IfStep>(s_if.WithBodies([s_if.Else, s_if.Then]));
        Assert.Equal<SequenceStep>([s_set, s_elseRestart], swapped.Then);
        Assert.Throws<ArgumentException>(() => s_if.WithBodies([s_if.Then]));
        Assert.Throws<ArgumentException>(() => s_end.WithBodies([[]]));
        Assert.Same(s_end, s_end.WithBodies([]));
    }

    // The frozen names of versions 1 and 2 are all in the catalogue, and nothing else is among them.
    [Fact]
    public void KeepsTheNamesOfVersion1AndTypesEveryName()
    {
        Assert.Equal<string>(
            [
                MachineVariableNames.Manufacturer,
                MachineVariableNames.Model,
                MachineVariableNames.SerialNumber,
                MachineVariableNames.SmbiosUuid,
                MachineVariableNames.MacAddress,
                MachineVariableNames.ComputerName,
                MachineVariableNames.Phase,
            ],
            MachineVariableNames.All);
        Assert.All(MachineVariableNames.All, name => Assert.True(MachineVariableNames.Catalogue.ContainsKey(name), name));
        Assert.All(MachineVariableNames.ChangeDuringRun, name => Assert.True(MachineVariableNames.Catalogue.ContainsKey(name), name));
        Assert.Equal(FactType.Mac, MachineVariableNames.Catalogue[MachineVariableNames.MacAddress]);
        Assert.Equal(FactType.Number, MachineVariableNames.Catalogue[MachineVariableNames.LastExitCode]);
        Assert.Equal(MachineVariableNames.Manufacturer, MachineVariableNames.Catalogue.Keys.First());
    }

    private static RebootStep Restart() => new() { Id = NodeId(99), Name = "Restart" };

    private static SequenceDefinition Flat(params SequenceStep[] steps) => new(SequenceDefinition.CurrentVersion, steps);

    private static SequenceStep Folded(ConditionNode when) => Assert.Single(Flat(Restart() with { When = when }).Normalised().Steps);

    private static Guid NodeId(int number) =>
        new(string.Create(CultureInfo.InvariantCulture, $"00000000-0000-4000-8000-{number:D12}"));
}

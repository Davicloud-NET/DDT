// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DDT.Contracts;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Core.Tests.Sequences;

// Version 3 documents and tree run states are stored and sent as JSON, so these strings never change either. Members
// version 3 adds to older kinds are left out while unset, so the strings of SequenceWireFormatTests stay the same.
public sealed class TreeWireFormatTests
{
    private const string Id = "0197a3c0-0000-7000-8000-000000000001";
    private const string Other = "0197a3c0-0000-7000-8000-000000000002";
    private const string Inner = "0197a3c0-0000-7000-8000-000000000003";
    private const string Base = $$"""
        "id":"{{Id}}","name":"Node","conditions":[],"continueOnError":false,"rebootAfter":false
        """;
    private const string Restart = $$"""
        {"kind":"reboot","id":"{{Inner}}","name":"Restart","conditions":[],"continueOnError":false,"rebootAfter":false}
        """;

    private static readonly Guid s_id = Guid.Parse(Id);
    private static readonly Guid s_other = Guid.Parse(Other);
    private static readonly RebootStep s_restart = new() { Id = Guid.Parse(Inner), Name = "Restart" };
    private static readonly TestCondition s_latitude = new(MachineVariableNames.Model, ConditionOperator.Contains, "Latitude");
    private static readonly string s_latitudeJson = """{"kind":"test","variable":"Model","operator":"Contains","value":"Latitude"}""";

    private static readonly Dictionary<string, (SequenceStep Step, string Json)> s_kinds = new(StringComparer.Ordinal)
    {
        ["group"] = (
            new GroupStep { Id = s_id, Name = "Node", Steps = [s_restart] },
            $$"""{"kind":"group","steps":[{{Restart}}],{{Base}}}"""),
        ["if"] = (
            new IfStep { Id = s_id, Name = "Node", Test = s_latitude, Then = [s_restart] },
            $$"""{"kind":"if","test":{{s_latitudeJson}},"then":[{{Restart}}],"else":[],{{Base}}}"""),
        ["repeat"] = (
            new RepeatStep { Id = s_id, Name = "Node", Steps = [s_restart], Until = s_latitude, MaxTimes = 5, GoOnAtLimit = true },
            $$"""{"kind":"repeat","steps":[{{Restart}}],"until":{{s_latitudeJson}},"maxTimes":5,"goOnAtLimit":true,{{Base}}}"""),
        ["setVariable"] = (
            new SetVariableStep { Id = s_id, Name = "Node", Variable = "Office", Value = "{{Office|upper}}" },
            $$$"""{"kind":"setVariable","variable":"Office","value":"{{Office|upper}}",{{{Base}}}}"""),
        ["pause"] = (
            new PauseStep { Id = s_id, Name = "Node", Message = "Check the BIOS.", ContinueAfterMinutes = 30 },
            $$"""{"kind":"pause","message":"Check the BIOS.","continueAfterMinutes":30,{{Base}}}"""),
        ["when and shares"] = (
            new RebootStep
            {
                Id = s_id,
                Name = "Node",
                When = new NoneCondition { Parts = [s_latitude] },
                Shares = [new ShareConnection(@"\\files.corp.example\drivers", new AccountReference(s_other, null))],
            },
            $$$"""{"kind":"reboot",{{{Base}}},"when":{"kind":"none","parts":[{{{s_latitudeJson}}}]},"shares":[{"path":"\\\\files.corp.example\\drivers","account":{"accountId":"{{{Other}}}","input":null}}]}"""),
        ["runAs"] = (
            new RunScriptStep { Id = s_id, Name = "Node", Phase = SequencePhase.Windows, Script = "exit 0", RunAs = new AccountReference(null, "Installer") },
            $$"""{"kind":"runScript","phase":"Windows","interpreter":"Cmd","script":"exit 0","packageId":null,"timeoutMinutes":60,"successExitCodes":[0],"rebootExitCodes":[3010],"runAs":{"accountId":null,"input":"Installer"},{{Base}}}"""),
        ["account"] = (
            new JoinDomainStep { Id = s_id, Name = "Node", Account = new AccountReference(s_other, null) },
            $$"""{"kind":"joinDomain","organizationalUnit":null,"account":{"accountId":"{{Other}}","input":null},{{Base}}}"""),
    };

    private static readonly Dictionary<string, (ConditionNode Condition, string Json)> s_conditions = new(StringComparer.Ordinal)
    {
        ["all"] = (new AllCondition { Parts = [s_latitude] }, $$"""{"kind":"all","parts":[{{s_latitudeJson}}]}"""),
        ["any"] = (new AnyCondition { Parts = [s_latitude] }, $$"""{"kind":"any","parts":[{{s_latitudeJson}}]}"""),
        ["none"] = (new NoneCondition(), """{"kind":"none","parts":[]}"""),
        ["test"] = (s_latitude, s_latitudeJson),
        ["nested"] = (
            new AnyCondition { Parts = [new AllCondition { Parts = [new TestCondition(MachineVariableNames.TpmPresent, ConditionOperator.Exists)] }] },
            """{"kind":"any","parts":[{"kind":"all","parts":[{"kind":"test","variable":"TpmPresent","operator":"Exists","value":""}]}]}"""),
    };

    public static TheoryData<string> Kinds => [.. s_kinds.Keys];

    public static TheoryData<string> ConditionKinds => [.. s_conditions.Keys];

    public static TheoryData<string> Contexts => ["agent", "web"];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void WritesAndReadsEveryNodeExactly(string kind)
    {
        (SequenceStep step, string json) = s_kinds[kind];

        AssertWritesAndReads(step, json, AgentJsonContext.Default.SequenceStep);
        AssertWritesAndReads(step, json, DdtJsonContext.Default.SequenceStep);
    }

    [Theory]
    [MemberData(nameof(ConditionKinds))]
    public void WritesAndReadsEveryConditionExactly(string kind)
    {
        (ConditionNode condition, string json) = s_conditions[kind];

        foreach (JsonTypeInfo<ConditionNode> typeInfo in new[] { AgentJsonContext.Default.ConditionNode, DdtJsonContext.Default.ConditionNode })
        {
            Assert.Equal(json, JsonSerializer.Serialize(condition, typeInfo));

            ConditionNode read = JsonSerializer.Deserialize(json, typeInfo) ?? throw new InvalidOperationException("Nothing read.");
            Assert.Equal(condition.GetType(), read.GetType());
            Assert.Equal(json, JsonSerializer.Serialize(read, typeInfo));
        }
    }

    // PostgreSQL jsonb and browsers may put "kind" last, at every depth.
    [Theory]
    [MemberData(nameof(Contexts))]
    public void ReadsKindsThatAreNotTheFirstPropertyAtEveryDepth(string context)
    {
        SequenceStep? step = JsonSerializer.Deserialize(
            $$"""
            {"id":"{{Id}}","name":"Choose","test":{"parts":[{"variable":"Model","operator":"Matches","value":"Latitude*","kind":"test"}],"kind":"any"},
             "then":[{"id":"{{Other}}","name":"Group","steps":[{"id":"{{Inner}}","name":"Restart","kind":"reboot"}],"kind":"group"}],"kind":"if"}
            """,
            StepTypeInfo(context));

        IfStep choose = Assert.IsType<IfStep>(step);
        TestCondition test = Assert.IsType<TestCondition>(Assert.Single(Assert.IsType<AnyCondition>(choose.Test).Parts));
        Assert.Equal(ConditionOperator.Matches, test.Operator);
        GroupStep group = Assert.IsType<GroupStep>(Assert.Single(choose.Then));
        Assert.Equal(Guid.Parse(Inner), Assert.IsType<RebootStep>(Assert.Single(group.Steps)).Id);
        Assert.Empty(Assert.Single(choose.Bodies, body => body.Name == StepBody.ElseName).Steps);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void RefusesAConditionOfAnUnknownKind(string context)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            $$$"""{"kind":"group","id":"{{{Id}}}","name":"Group","when":{"kind":"xor","parts":[]}}""",
            StepTypeInfo(context)));
    }

    [Fact]
    public void WritesAFlatDefinitionWithoutTheMembersOfVersion3()
    {
        SequenceDefinition definition = new(1, [new RebootStep { Id = s_id, Name = "Node" }]);

        Assert.Equal(
            $$"""{"version":1,"steps":[{"kind":"reboot",{{Base}}}]}""",
            JsonSerializer.Serialize(definition, DdtJsonContext.Default.SequenceDefinition));
    }

    [Fact]
    public void WritesAndReadsVariablesAndInputs()
    {
        SequenceDefinition definition = new(3, [new RebootStep { Id = s_id, Name = "Node" }])
        {
            Variables = [new VariableDeclaration { Name = "Office", Default = "Standard", SetBySteps = true }],
            Inputs =
            [
                new InputDeclaration
                {
                    Name = "Office",
                    Label = "Office edition",
                    Kind = InputKind.Choice,
                    Choices = [new InputChoice("Standard"), new InputChoice("ProPlus", "Professional Plus")],
                    Required = true,
                },
                new InputDeclaration
                {
                    Name = "JoinAccount",
                    Label = "Join account",
                    Kind = InputKind.Account,
                    AskAt = InputAsk.Machine,
                    Account = new AccountDestination { Domain = "corp.example" },
                },
            ],
        };
        string json =
            $$"""{"version":3,"steps":[{"kind":"reboot",{{Base}}}],"variables":[{"name":"Office","default":"Standard","description":null,"setBySteps":true}],"inputs":[""" +
            """{"name":"Office","label":"Office edition","help":null,"kind":"Choice","choices":[{"value":"Standard","label":null},{"value":"ProPlus","label":"Professional Plus"}],"default":null,"required":true,"maxLength":null,"askAt":"Both","account":null},""" +
            """{"name":"JoinAccount","label":"Join account","help":null,"kind":"Account","choices":[],"default":null,"required":false,"maxLength":null,"askAt":"Machine","account":{"domain":"corp.example","hosts":[],"runAs":false}}]}""";

        foreach (JsonTypeInfo<SequenceDefinition> typeInfo in new[] { AgentJsonContext.Default.SequenceDefinition, DdtJsonContext.Default.SequenceDefinition })
        {
            Assert.Equal(json, JsonSerializer.Serialize(definition, typeInfo));

            SequenceDefinition read = JsonSerializer.Deserialize(json, typeInfo) ?? throw new InvalidOperationException("Nothing read.");
            Assert.Equal(json, JsonSerializer.Serialize(read, typeInfo));
            Assert.Equal(InputAsk.Both, read.Inputs![0].AskAt);
        }
    }

    // As an agent of version 1 or 2 saved it: no cursor, and no member of a tree on any step.
    [Fact]
    public void ReadsAStateOfFormat1()
    {
        string json =
            $$$"""{"format":1,"runId":"{{{Other}}}","definition":{"version":1,"steps":[{"kind":"reboot",{{{Base}}}}]},"phase":"WindowsPE","nextIndex":1,"steps":[{"stepId":"{{{Id}}}","state":"Done","error":null}],"variables":{}}""";

        SequenceState state = JsonSerializer.Deserialize(json, AgentJsonContext.Default.SequenceState)
            ?? throw new InvalidOperationException("The state did not read.");

        Assert.Equal(SequenceState.CurrentFormat, state.Format);
        Assert.Null(state.Cursor);
        StepRunState step = Assert.Single(state.Steps);
        Assert.Equal(new StepRunState(s_id, StepState.Done, null), step);
        Assert.Equal(0, step.Pass);
        Assert.Equal(0, step.Iteration);
        Assert.Null(step.Branch);
        Assert.Null(step.Evaluation);
        Assert.Equal(json, JsonSerializer.Serialize(state, AgentJsonContext.Default.SequenceState));
    }

    [Fact]
    public void WritesAndReadsAStateOfFormat2()
    {
        IfStep choose = new() { Id = s_id, Name = "Node", Test = s_latitude, Then = [s_restart] };
        SequenceState state = new(
            SequenceState.TreeFormat,
            s_other,
            new SequenceDefinition(3, [choose]),
            SequencePhase.WindowsPE,
            0,
            [
                new StepRunState(s_id, StepState.Running, null, Pass: 1, Branch: IfBranch.Then, Evaluation: [new TestEvaluation("test", true, "Latitude 7440")]),
                new StepRunState(s_restart.Id, StepState.Pending, null),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal),
            new NodeCursor(s_restart.Id, false));
        string json =
            $$"""{"format":2,"runId":"{{Other}}","definition":{"version":3,"steps":[{"kind":"if","test":{{s_latitudeJson}},"then":[{{Restart}}],"else":[],{{Base}}}]},""" +
            $$$"""
            "phase":"WindowsPE","nextIndex":0,"steps":[{"stepId":"{{{Id}}}","state":"Running","error":null,"pass":1,"branch":"Then","evaluation":[{"path":"test","held":true,"actual":"Latitude 7440"}]},{"stepId":"{{{Inner}}}","state":"Pending","error":null}],"variables":{},"cursor":{"nodeId":"{{{Inner}}}","leaving":false}}
            """;

        Assert.Equal(json, JsonSerializer.Serialize(state, AgentJsonContext.Default.SequenceState));

        SequenceState read = JsonSerializer.Deserialize(json, AgentJsonContext.Default.SequenceState)
            ?? throw new InvalidOperationException("The state did not read back.");
        Assert.Equal(new NodeCursor(s_restart.Id, false), read.Cursor);
        Assert.Equal(json, JsonSerializer.Serialize(read, AgentJsonContext.Default.SequenceState));
    }

    // The source generator sets every init member, so one the JSON leaves out reads as its type's default, not as the
    // value C# gives it: the validator and the tree have to cope with 0 and null. Bodies stand in for a missing list.
    [Theory]
    [MemberData(nameof(Contexts))]
    public void ReadsAMemberLeftOutAsItsTypesDefault(string context)
    {
        RepeatStep repeat = Assert.IsType<RepeatStep>(JsonSerializer.Deserialize(
            $$$"""{"kind":"repeat","id":"{{{Id}}}","name":"Again","until":{"kind":"test","variable":"TpmPresent","operator":"Exists"}}""",
            StepTypeInfo(context)));

        Assert.Equal(0, repeat.MaxTimes);
        Assert.Null(repeat.Steps);
        Assert.Empty(Assert.Single(repeat.Bodies).Steps);
        Assert.Equal("", Assert.IsType<TestCondition>(repeat.Until).Value);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            $$"""{"kind":"repeat","id":"{{Id}}","name":"Again"}""",
            StepTypeInfo(context)));
    }

    private static void AssertWritesAndReads(SequenceStep step, string json, JsonTypeInfo<SequenceStep> typeInfo)
    {
        Assert.Equal(json, JsonSerializer.Serialize(step, typeInfo));

        SequenceStep? read = JsonSerializer.Deserialize(json, typeInfo);
        Assert.NotNull(read);
        Assert.Equal(step.GetType(), read.GetType());
        Assert.Equal(json, JsonSerializer.Serialize(read, typeInfo));
    }

    private static JsonTypeInfo<SequenceStep> StepTypeInfo(string context) =>
        context == "agent" ? AgentJsonContext.Default.SequenceStep : DdtJsonContext.Default.SequenceStep;
}

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

// Sequences are stored, frozen into runs and persisted by agents as JSON, so these strings never change.
public sealed class SequenceWireFormatTests
{
    private const string Id = "0197a3c0-0000-7000-8000-000000000001";
    private const string Other = "0197a3c0-0000-7000-8000-000000000002";

    private static readonly Guid s_id = Guid.Parse(Id);
    private static readonly Guid s_other = Guid.Parse(Other);

    private static readonly Dictionary<string, (SequenceStep Step, string Json)> s_kinds = new(StringComparer.Ordinal)
    {
        ["partition"] = (
            new PartitionStep { Id = s_id, Name = "Partition" },
            $$"""{"kind":"partition","systemPartitionMegabytes":300,"recoveryPartitionMegabytes":1024,"id":"{{Id}}","name":"Partition","conditions":[],"continueOnError":false,"rebootAfter":false}"""),
        ["applyImage"] = (
            new ApplyImageStep { Id = s_id, Name = "Apply", ImageId = s_other },
            $$"""{"kind":"applyImage","imageId":"{{Other}}","id":"{{Id}}","name":"Apply","conditions":[],"continueOnError":false,"rebootAfter":false}"""),
        ["injectDrivers"] = (
            new InjectDriversStep { Id = s_id, Name = "Drivers", RequireMatch = true },
            $$"""{"kind":"injectDrivers","requireMatch":true,"id":"{{Id}}","name":"Drivers","conditions":[],"continueOnError":false,"rebootAfter":false}"""),
        ["writeUnattend"] = (
            new WriteUnattendStep { Id = s_id, Name = "Answer file", TimeZone = "W. Europe Standard Time", LocalAdministrator = true },
            $$"""{"kind":"writeUnattend","timeZone":"W. Europe Standard Time","locale":null,"keyboard":null,"localAdministrator":true,"id":"{{Id}}","name":"Answer file","conditions":[],"continueOnError":false,"rebootAfter":false}"""),
        ["joinDomain"] = (
            new JoinDomainStep { Id = s_id, Name = "Join", OrganizationalUnit = "OU=Clients,DC=corp,DC=example", RebootAfter = true },
            $$"""{"kind":"joinDomain","organizationalUnit":"OU=Clients,DC=corp,DC=example","id":"{{Id}}","name":"Join","conditions":[],"continueOnError":false,"rebootAfter":true}"""),
        ["runScript"] = (
            new RunScriptStep
            {
                Id = s_id,
                Name = "Script",
                Phase = SequencePhase.Windows,
                Interpreter = ScriptInterpreter.PowerShell,
                Script = "exit 0",
                PackageId = s_other,
                Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.StartsWith, "Latitude")],
                ContinueOnError = true,
            },
            $$"""{"kind":"runScript","phase":"Windows","interpreter":"PowerShell","script":"exit 0","packageId":"{{Other}}","timeoutMinutes":60,"successExitCodes":[0],"rebootExitCodes":[3010],"id":"{{Id}}","name":"Script","conditions":[{"variable":"Model","operator":"StartsWith","value":"Latitude"}],"continueOnError":true,"rebootAfter":false}"""),
        ["reboot"] = (
            new RebootStep { Id = s_id, Name = "Restart" },
            $$"""{"kind":"reboot","id":"{{Id}}","name":"Restart","conditions":[],"continueOnError":false,"rebootAfter":false}"""),
    };

    public static TheoryData<string> Kinds => [.. s_kinds.Keys];

    public static TheoryData<string> Contexts => ["agent", "web"];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void WritesAndReadsEveryKindExactly(string kind)
    {
        (SequenceStep step, string json) = s_kinds[kind];

        AssertWritesAndReads(step, json, AgentJsonContext.Default.SequenceStep);
        AssertWritesAndReads(step, json, DdtJsonContext.Default.SequenceStep);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void ReadsAKindThatIsNotTheFirstProperty(string context)
    {
        SequenceStep? step = JsonSerializer.Deserialize(
            $$"""{"id":"{{Id}}","name":"Restart","kind":"reboot"}""",
            StepTypeInfo(context));

        RebootStep reboot = Assert.IsType<RebootStep>(step);
        Assert.Equal(s_id, reboot.Id);
        Assert.Equal("Restart", reboot.Name);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void RefusesAStepWithoutAnId(string context)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"kind":"reboot","name":"Restart"}""", StepTypeInfo(context)));
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void RefusesAnUnknownKind(string context)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            $$"""{"kind":"formatDisk","id":"{{Id}}","name":"Format"}""",
            StepTypeInfo(context)));
    }

    // Not a JsonException: whoever parses a sequence from outside has to catch both.
    [Theory]
    [MemberData(nameof(Contexts))]
    public void RefusesAStepWithoutAKind(string context)
    {
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize(
            $$"""{"id":"{{Id}}","name":"Restart"}""",
            StepTypeInfo(context)));
    }

    [Fact]
    public void WritesADefinitionWithItsVersionAndSteps()
    {
        SequenceDefinition definition = new(SequenceDefinition.CurrentVersion, [new RebootStep { Id = s_id, Name = "Restart" }]);

        Assert.Equal(
            $$"""{"version":1,"steps":[{"kind":"reboot","id":"{{Id}}","name":"Restart","conditions":[],"continueOnError":false,"rebootAfter":false}]}""",
            JsonSerializer.Serialize(definition, AgentJsonContext.Default.SequenceDefinition));
    }

    [Fact]
    public void WritesAndReadsTheRunState()
    {
        SequenceState state = new(
            SequenceState.CurrentFormat,
            s_other,
            new SequenceDefinition(SequenceDefinition.CurrentVersion, [new RebootStep { Id = s_id, Name = "Restart" }]),
            SequencePhase.WindowsPE,
            1,
            [new StepRunState(s_id, StepState.Failed, "It broke.")],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["ddt.disk.windows"] = "abc" });
        string json =
            $$$"""{"format":1,"runId":"{{{Other}}}","definition":{"version":1,"steps":[{"kind":"reboot","id":"{{{Id}}}","name":"Restart","conditions":[],"continueOnError":false,"rebootAfter":false}]},"phase":"WindowsPE","nextIndex":1,"steps":[{"stepId":"{{{Id}}}","state":"Failed","error":"It broke."}],"variables":{"ddt.disk.windows":"abc"}}""";

        Assert.Equal(json, JsonSerializer.Serialize(state, AgentJsonContext.Default.SequenceState));

        SequenceState read = JsonSerializer.Deserialize(json, AgentJsonContext.Default.SequenceState)
            ?? throw new InvalidOperationException("The state did not read back.");
        Assert.Equal(json, JsonSerializer.Serialize(read, AgentJsonContext.Default.SequenceState));
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

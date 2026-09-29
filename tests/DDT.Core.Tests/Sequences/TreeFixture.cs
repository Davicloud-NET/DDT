// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Core.Tests.Sequences;

// A tree with a group, IFs on the model and on a variable a step sets, a retry that restarts and works the second time,
// a repeat that continues at its limit, a group that catches a failure, and a hand-over to Windows on one branch.
internal sealed class TreeFixture
{
    public const string Office = "Office";
    public const string Tries = "tries";

    public static int Count(StepContext context) =>
        int.Parse(context.Variables.GetValueOrDefault(Tries, "0"), NumberStyles.None, CultureInfo.InvariantCulture);

    public static Dictionary<string, string> Outputs(params (string Name, string Value)[] outputs) =>
        outputs.ToDictionary(output => output.Name, output => output.Value, StringComparer.Ordinal);

    public static SequenceDefinition Tree(params SequenceStep[] steps) =>
        new(3, steps) { Variables = [new VariableDeclaration { Name = Office, SetBySteps = true }] };

    public static RunScriptStep Script(string name, SequencePhase phase = SequencePhase.WindowsPE) =>
        new() { Id = Guid.NewGuid(), Name = name, Phase = phase, Script = "exit /b 0" };

    public static RebootStep Reboot(string name) => new() { Id = Guid.NewGuid(), Name = name };

    public PartitionStep Partition { get; } = new() { Id = Guid.NewGuid(), Name = "Partition" };

    public SetVariableStep SetOffice { get; } = new()
    {
        Id = Guid.NewGuid(),
        Name = "Set the office",
        Variable = "office",
        Value = "{{Site|upper}}-{{Model|alnum|right:4}}",
    };

    public RunScriptStep PrepareA { get; } = Script("Prepare a");

    public RunScriptStep OnlyPrecision { get; } = Script("Only on a Precision") with
    {
        When = new TestCondition(MachineVariableNames.Model, ConditionOperator.Contains, "Precision"),
    };

    public GroupStep Prepare => new() { Id = PrepareId, Name = "Prepare", Steps = [PrepareA, OnlyPrecision] };

    public RunScriptStep RestartingInThen { get; } = Script("Restarting in Then") with { RebootAfter = true };

    public RunScriptStep AfterRestart { get; } = Script("After the restart");

    public RunScriptStep NotLatitude { get; } = Script("Not a Latitude");

    public IfStep ByModel => new()
    {
        Id = ByModelId,
        Name = "By model",
        Test = new TestCondition(MachineVariableNames.FriendlyModel, ConditionOperator.Contains, "Latitude"),
        Then = [RestartingInThen, AfterRestart],
        Else = [NotLatitude],
    };

    public RunScriptStep Berlin { get; } = Script("Berlin");

    public RunScriptStep Elsewhere { get; } = Script("Elsewhere");

    public IfStep ByOffice => new()
    {
        Id = ByOfficeId,
        Name = "By office",
        Test = new TestCondition(Office, ConditionOperator.Equals, "BERLIN-7440"),
        Then = [Berlin],
        Else = [Elsewhere],
    };

    public RunScriptStep Try { get; } = Script("Try") with { ContinueOnError = true };

    public RebootStep RestartInRetry { get; } = Reboot("Restart in the retry");

    public RepeatStep Retry => new()
    {
        Id = RetryId,
        Name = "Retry",
        Until = new TestCondition(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "0"),
        MaxTimes = 3,
        Steps = [Try, RestartInRetry],
    };

    public RunScriptStep Twice { get; } = Script("Twice");

    public RepeatStep AtMostTwice => new()
    {
        Id = AtMostTwiceId,
        Name = "At most twice",
        Until = new TestCondition(Office, ConditionOperator.Equals, "nowhere"),
        MaxTimes = 2,
        GoOnAtLimit = true,
        Steps = [Twice],
    };

    public RunScriptStep Failing { get; } = Script("Failing");

    public RunScriptStep NeverReached { get; } = Script("Never reached");

    public GroupStep Guarded => new() { Id = GuardedId, Name = "Guarded", ContinueOnError = true, Steps = [Failing, NeverReached] };

    public RunScriptStep InWindows { get; } = Script("In Windows", SequencePhase.Windows);

    public RunScriptStep InWindowsPE { get; } = Script("In Windows PE");

    public IfStep BySite => new()
    {
        Id = BySiteId,
        Name = "By site",
        Test = new TestCondition("Site", ConditionOperator.Equals, "vienna"),
        Then = [InWindows],
        Else = [InWindowsPE],
    };

    public RebootStep Last { get; } = Reboot("Last");

    public SequenceDefinition Definition =>
        Tree(Partition, SetOffice, Prepare, ByModel, ByOffice, Retry, AtMostTwice, Guarded, BySite, Last);

    public IReadOnlyDictionary<Guid, Func<StepContext, StepResult>> Behaviours => new Dictionary<Guid, Func<StepContext, StepResult>>
    {
        [Try.Id] = context =>
        {
            int tries = Count(context) + 1;
            Dictionary<string, string> outputs = Outputs((Tries, tries.ToString(CultureInfo.InvariantCulture)));

            return tries < 2
                ? new StepResult(StepOutcome.Failed, "The script ended with exit code 1.", outputs) { ExitCode = 1 }
                : StepResult.Done(outputs) with { ExitCode = 0 };
        },
        [Failing.Id] = _ => StepResult.Failed("The script ended with exit code 5.") with { ExitCode = 5 },
    };

    private Guid PrepareId { get; } = Guid.NewGuid();

    private Guid ByModelId { get; } = Guid.NewGuid();

    private Guid ByOfficeId { get; } = Guid.NewGuid();

    private Guid RetryId { get; } = Guid.NewGuid();

    private Guid AtMostTwiceId { get; } = Guid.NewGuid();

    private Guid GuardedId { get; } = Guid.NewGuid();

    private Guid BySiteId { get; } = Guid.NewGuid();

    public SequenceState Start() => SequenceStates.Start(Guid.NewGuid(), Definition);
}

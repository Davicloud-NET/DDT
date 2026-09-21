// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Core.Tests.Sequences;

public sealed class SequenceValidatorTests
{
    [Fact]
    public void AcceptsASequenceThroughBothPhases()
    {
        IReadOnlyList<SequenceProblem> problems = Validate(
            Partition(),
            Script(SequencePhase.WindowsPE) with { PackageId = Guid.NewGuid() },
            Reboot(),
            ApplyImage(),
            InjectDrivers(),
            WriteUnattend() with { LocalAdministrator = true },
            Script(SequencePhase.Windows) with { Interpreter = ScriptInterpreter.PowerShell },
            Reboot(),
            JoinDomain() with { RebootAfter = true });

        Assert.Empty(problems);
    }

    [Fact]
    public void AllowsPowerShellInWindowsPE()
    {
        Assert.Empty(Validate(Partition(), Script(SequencePhase.WindowsPE) with { Interpreter = ScriptInterpreter.PowerShell }));
    }

    [Fact]
    public void RefusesAnotherVersion()
    {
        IReadOnlyList<SequenceProblem> problems = SequenceValidator.Validate(new SequenceDefinition(2, [Partition()]));

        SequenceProblem problem = Assert.Single(problems);
        Assert.Null(problem.StepId);
        Assert.Equal("version", problem.Field);
    }

    [Fact]
    public void RefusesNoStepsAndMoreThanAHundred()
    {
        SequenceStep[] tooMany = [Partition(), .. Enumerable.Range(0, SequenceValidator.MaxSteps).Select(_ => Reboot())];

        Assert.Equal("steps", Assert.Single(Validate()).Field);
        Assert.Equal("steps", Assert.Single(Validate(tooMany)).Field);
        Assert.Empty(Validate(tooMany[..SequenceValidator.MaxSteps]));
    }

    [Fact]
    public void RefusesAnEmptyStep()
    {
        SequenceProblem problem = Assert.Single(Validate(Partition(), null!));

        Assert.Null(problem.StepId);
        Assert.Equal("steps", problem.Field);
    }

    [Fact]
    public void RefusesAMissingOrRepeatedId()
    {
        PartitionStep partition = Partition();

        SequenceProblem missing = Assert.Single(Validate(partition with { Id = Guid.Empty }));
        Assert.Null(missing.StepId);
        Assert.Equal("id", missing.Field);

        AssertOnlyProblem(Validate(partition, Reboot() with { Id = partition.Id }), partition, "id");
    }

    [Fact]
    public void RefusesAMissingOrTooLongName()
    {
        foreach (string? name in (string?[])[null, "", "   ", new string('n', SequenceValidator.MaxNameLength + 1)])
        {
            PartitionStep partition = Partition() with { Name = name! };

            AssertOnlyProblem(Validate(partition), partition, "name");
        }

        Assert.Empty(Validate(Partition() with { Name = new string('n', SequenceValidator.MaxNameLength) }));
    }

    [Fact]
    public void RefusesMoreThanTenConditions()
    {
        StepCondition condition = new(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 5440");
        RebootStep reboot = Reboot() with { Conditions = [.. Enumerable.Repeat(condition, SequenceValidator.MaxConditions + 1)] };

        AssertOnlyProblem(Validate(Partition(), reboot), reboot, "conditions");
        Assert.Empty(Validate(Partition(), reboot with { Conditions = reboot.Conditions.Skip(1).ToArray() }));
    }

    [Theory]
    [InlineData("BiosVersion", ConditionOperator.Equals, "1.0", "conditions[1].variable")]
    [InlineData("model", ConditionOperator.Equals, "Latitude", "conditions[1].variable")]
    [InlineData(MachineVariableNames.Model, (ConditionOperator)9, "Latitude", "conditions[1].operator")]
    [InlineData(MachineVariableNames.Model, ConditionOperator.Equals, "", "conditions[1].value")]
    [InlineData(MachineVariableNames.Model, ConditionOperator.Equals, " ", "conditions[1].value")]
    public void RefusesAConditionWithAnUnknownVariableOrOperatorOrNoValue(
        string variable,
        ConditionOperator @operator,
        string value,
        string field)
    {
        RebootStep reboot = Reboot() with
        {
            Conditions =
            [
                new StepCondition(MachineVariableNames.Phase, ConditionOperator.Equals, "WindowsPE"),
                new StepCondition(variable, @operator, value),
            ],
        };

        AssertOnlyProblem(Validate(Partition(), reboot), reboot, field);
    }

    [Theory]
    [InlineData(ConditionOperator.Contains, ":")]
    [InlineData(ConditionOperator.StartsWith, "- -")]
    [InlineData(ConditionOperator.StartsWith, "00:15:5D:01:02:03:04")]
    [InlineData(ConditionOperator.Equals, "00:15:5D:01:02")]
    [InlineData(ConditionOperator.NotEquals, "00:15:5D:01:02:O3")]
    public void RefusesAMacAddressThatIsTooShortTooLongOrNotHex(ConditionOperator @operator, string value)
    {
        RebootStep reboot = Reboot() with { Conditions = [new StepCondition(MachineVariableNames.MacAddress, @operator, value)] };

        AssertOnlyProblem(Validate(Partition(), reboot), reboot, "conditions[0].value");
        Assert.Empty(Validate(Partition(), reboot with
        {
            Conditions = [new StepCondition(MachineVariableNames.MacAddress, @operator, "00-15-5d-01-02-03")],
        }));
    }

    [Fact]
    public void AcceptsPartOfAMacAddressForStartsWithAndContains()
    {
        RebootStep reboot = Reboot() with
        {
            Conditions =
            [
                new StepCondition(MachineVariableNames.MacAddress, ConditionOperator.StartsWith, "00:15:5D"),
                new StepCondition(MachineVariableNames.MacAddress, ConditionOperator.Contains, "0a"),
            ],
        };

        Assert.Empty(Validate(Partition(), reboot));
    }

    [Fact]
    public void RefusesMissingConditions()
    {
        RebootStep missing = Reboot() with { Conditions = null! };
        RebootStep empty = Reboot() with { Conditions = [null!] };

        AssertOnlyProblem(Validate(Partition(), missing), missing, "conditions");
        AssertOnlyProblem(Validate(Partition(), empty), empty, "conditions[0]");
    }

    [Fact]
    public void RefusesAnUnknownKind()
    {
        WriteRawImageStep raw = new() { Id = Guid.NewGuid(), Name = "Write the raw image" };

        AssertOnlyProblem(Validate(Partition(), raw), raw, "kind");
    }

    [Fact]
    public void RefusesAWindowsPEStepAfterAWindowsStep()
    {
        RunScriptStep late = Script(SequencePhase.WindowsPE);

        AssertOnlyProblem(Validate(Partition(), ApplyImage(), Script(SequencePhase.Windows), late), late, "phase");
    }

    [Fact]
    public void PartitionsOnlyOnce()
    {
        PartitionStep again = Partition();

        AssertOnlyProblem(Validate(Partition(), again), again, null);
    }

    [Fact]
    public void PartitionsEveryTime()
    {
        PartitionStep conditional = Partition() with
        {
            Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 5440")],
        };
        PartitionStep continuing = Partition() with { ContinueOnError = true };

        AssertOnlyProblem(Validate(conditional), conditional, "conditions");
        AssertOnlyProblem(Validate(continuing), continuing, "continueOnError");
    }

    [Fact]
    public void PartitionsBeforeTheImageIsApplied()
    {
        PartitionStep late = Partition();

        IReadOnlyList<SequenceProblem> problems = Validate(ApplyImage(), late);

        Assert.Contains(
            problems,
            problem => problem.StepId == late.Id && problem.Message == "The disk has to be partitioned before the image is applied.");
    }

    [Theory]
    [InlineData(259, 1024, "systemPartitionMegabytes")]
    [InlineData(4097, 1024, "systemPartitionMegabytes")]
    [InlineData(300, 299, "recoveryPartitionMegabytes")]
    [InlineData(300, 65537, "recoveryPartitionMegabytes")]
    public void RefusesAPartitionSizeOutsideTheLimits(int system, int recovery, string field)
    {
        PartitionStep partition = Partition() with { SystemPartitionMegabytes = system, RecoveryPartitionMegabytes = recovery };

        AssertOnlyProblem(Validate(partition), partition, field);
        Assert.Empty(Validate(Partition() with { SystemPartitionMegabytes = 260, RecoveryPartitionMegabytes = 300 }));
        Assert.Empty(Validate(Partition() with { SystemPartitionMegabytes = 4096, RecoveryPartitionMegabytes = 65536 }));
    }

    [Fact]
    public void AppliesOnlyOneImage()
    {
        ApplyImageStep again = ApplyImage();

        AssertOnlyProblem(Validate(Partition(), ApplyImage(), again), again, null);
    }

    [Fact]
    public void AppliesTheImageAfterPartitioning()
    {
        ApplyImageStep apply = ApplyImage();

        AssertOnlyProblem(Validate(apply), apply, null);
    }

    [Fact]
    public void AppliesTheImageEveryTime()
    {
        ApplyImageStep conditional = ApplyImage() with
        {
            Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 5440")],
        };
        ApplyImageStep continuing = ApplyImage() with { ContinueOnError = true };

        AssertOnlyProblem(Validate(Partition(), conditional), conditional, "conditions");
        AssertOnlyProblem(Validate(Partition(), continuing), continuing, "continueOnError");
    }

    [Fact]
    public void InjectsDriversAfterTheImage()
    {
        InjectDriversStep drivers = InjectDrivers();

        AssertOnlyProblem(Validate(Partition(), drivers, ApplyImage()), drivers, null);
    }

    [Fact]
    public void WritesTheAnswerFileOnceAfterTheImage()
    {
        WriteUnattendStep early = WriteUnattend();
        WriteUnattendStep again = WriteUnattend();

        AssertOnlyProblem(Validate(Partition(), early, ApplyImage()), early, null);
        AssertOnlyProblem(Validate(Partition(), ApplyImage(), WriteUnattend(), again), again, null);
    }

    [Fact]
    public void JoinsTheDomainOnlyOnce()
    {
        JoinDomainStep again = JoinDomain();

        AssertOnlyProblem(Validate(Partition(), ApplyImage(), JoinDomain(), again), again, null);
    }

    [Fact]
    public void RunsWindowsStepsOnlyAfterTheImageWasAppliedEveryTime()
    {
        RunScriptStep script = Script(SequencePhase.Windows);
        JoinDomainStep join = JoinDomain();
        ApplyImageStep conditional = ApplyImage() with
        {
            Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 5440")],
        };

        AssertOnlyProblem(Validate(Partition(), script), script, "phase");

        IReadOnlyList<SequenceProblem> problems = Validate(Partition(), conditional, join);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, problem => problem.StepId == join.Id && problem.Field is null);
    }

    [Theory]
    [InlineData("script")]
    [InlineData("timeoutMinutes")]
    [InlineData("successExitCodes")]
    [InlineData("rebootExitCodes")]
    [InlineData("phase")]
    [InlineData("interpreter")]
    public void KeepsScriptsWithinTheirLimits(string field)
    {
        RunScriptStep script = Script(SequencePhase.WindowsPE);
        int[] seventeen = [.. Enumerable.Range(1, SequenceValidator.MaxExitCodes + 1)];

        RunScriptStep[] wrong = field switch
        {
            "script" =>
            [
                script with { Script = "" },
                script with { Script = null! },
                script with { Script = new string('x', SequenceValidator.MaxScriptBytes + 1) },
                script with { Script = new string('ä', (SequenceValidator.MaxScriptBytes / 2) + 1) },
            ],
            "timeoutMinutes" =>
            [
                script with { TimeoutMinutes = 0 },
                script with { TimeoutMinutes = SequenceValidator.MaxTimeoutMinutes + 1 },
            ],
            "successExitCodes" =>
            [
                script with { SuccessExitCodes = [] },
                script with { SuccessExitCodes = seventeen },
                script with { SuccessExitCodes = null! },
            ],
            "rebootExitCodes" =>
            [
                script with { RebootExitCodes = seventeen },
                script with { RebootExitCodes = [0, 3010] },
                script with { RebootExitCodes = null! },
            ],
            "phase" => [script with { Phase = (SequencePhase)7 }],
            _ => [script with { Interpreter = (ScriptInterpreter)7 }],
        };

        foreach (RunScriptStep step in wrong)
        {
            AssertOnlyProblem(Validate(Partition(), step), step, field);
        }

        Assert.Empty(Validate(Partition(), script with
        {
            Script = new string('x', SequenceValidator.MaxScriptBytes),
            TimeoutMinutes = SequenceValidator.MaxTimeoutMinutes,
            SuccessExitCodes = seventeen[..SequenceValidator.MaxExitCodes],
            RebootExitCodes = [],
        }));
    }

    [Fact]
    public void RunsAWindowsPEScriptWithAPackageOnlyAfterPartitioning()
    {
        RunScriptStep script = Script(SequencePhase.WindowsPE) with { PackageId = Guid.NewGuid(), RebootExitCodes = [] };

        AssertOnlyProblem(Validate(script, Partition()), script, "packageId");
        Assert.Empty(Validate(Partition(), script));
    }

    [Fact]
    public void RestartsWindowsPEOnlyAfterPartitioning()
    {
        RebootStep reboot = Reboot();
        RunScriptStep restartAfter = Script(SequencePhase.WindowsPE) with { RebootExitCodes = [], RebootAfter = true };
        RunScriptStep restartCodes = Script(SequencePhase.WindowsPE);

        AssertOnlyProblem(Validate(reboot, Partition()), reboot, null);
        AssertOnlyProblem(Validate(restartAfter, Partition()), restartAfter, "rebootAfter");
        AssertOnlyProblem(Validate(restartCodes, Partition()), restartCodes, "rebootExitCodes");
        Assert.Empty(Validate(Partition() with { RebootAfter = true }, Reboot()));
    }

    [Fact]
    public void RefusesRestartAfterOnARestartStep()
    {
        RebootStep reboot = Reboot() with { RebootAfter = true };

        AssertOnlyProblem(Validate(Partition(), reboot), reboot, "rebootAfter");
    }

    [Fact]
    public void GivesAStepWithoutAPhaseThePhaseOfTheStepBeforeIt()
    {
        SequenceDefinition definition = new(
            SequenceDefinition.CurrentVersion,
            [Reboot(), Partition(), ApplyImage(), Reboot(), Script(SequencePhase.Windows), Reboot()]);

        SequencePhase pe = SequencePhase.WindowsPE;
        SequencePhase windows = SequencePhase.Windows;

        Assert.Equal(
            [pe, pe, pe, pe, windows, windows],
            Enumerable.Range(0, definition.Steps.Count).Select(index => SequencePhases.Of(definition, index)));
    }

    private static IReadOnlyList<SequenceProblem> Validate(params SequenceStep[] steps) =>
        SequenceValidator.Validate(new SequenceDefinition(SequenceDefinition.CurrentVersion, steps));

    private static void AssertOnlyProblem(IReadOnlyList<SequenceProblem> problems, SequenceStep step, string? field)
    {
        SequenceProblem problem = Assert.Single(problems);
        Assert.Equal(step.Id, problem.StepId);
        Assert.Equal(field, problem.Field);
    }

    private static PartitionStep Partition() => new() { Id = Guid.NewGuid(), Name = "Partition the disk" };

    private static ApplyImageStep ApplyImage() => new() { Id = Guid.NewGuid(), Name = "Apply the image", ImageId = Guid.NewGuid() };

    private static InjectDriversStep InjectDrivers() => new() { Id = Guid.NewGuid(), Name = "Add drivers" };

    private static WriteUnattendStep WriteUnattend() => new() { Id = Guid.NewGuid(), Name = "Write the answer file" };

    private static JoinDomainStep JoinDomain() => new() { Id = Guid.NewGuid(), Name = "Join the domain" };

    private static RunScriptStep Script(SequencePhase phase) =>
        new() { Id = Guid.NewGuid(), Name = "Run a script", Phase = phase, Script = "exit /b 0" };

    private static RebootStep Reboot() => new() { Id = Guid.NewGuid(), Name = "Restart" };
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using DDT.Server.Images;
using DDT.Server.Packages;
using DDT.Server.Sequences;
using Xunit;

namespace DDT.Server.Tests;

// What the server checks walks the whole tree: any branch may run, so every branch has to hold up.
public sealed class SequenceChecksTests
{
    private static readonly Image s_windows = new()
    {
        Id = Guid.NewGuid(),
        Name = "Windows 11",
        Kind = ImageKind.Wim,
        Sha256 = "",
        Architecture = "x64",
    };

    private static readonly Image s_raw = new()
    {
        Id = Guid.NewGuid(),
        Name = "Ubuntu",
        Kind = ImageKind.RawDisk,
        Sha256 = "",
        BootCapability = ImageBootCapability.SecureBootOk,
    };

    private static readonly Package s_drivers = new() { Id = Guid.NewGuid(), Name = "Drivers", Kind = PackageKind.Drivers, Sha256 = "" };

    private static SequenceReferences References(bool localAdministrator = true) => new(
        new Dictionary<Guid, Image> { [s_windows.Id] = s_windows, [s_raw.Id] = s_raw },
        new Dictionary<Guid, Package> { [s_drivers.Id] = s_drivers },
        DomainConfigured: true,
        LocalAdministratorConfigured: localAdministrator);

    private static SequenceDefinition Definition(params SequenceStep[] steps) => new(SequenceDefinition.CurrentVersion, steps);

    private static IfStep If(IReadOnlyList<SequenceStep> then, IReadOnlyList<SequenceStep> otherwise) => new()
    {
        Id = Guid.NewGuid(),
        Name = "If a ThinkPad",
        Test = new TestCondition(MachineVariableNames.FriendlyModel, ConditionOperator.Matches, "ThinkPad*"),
        Then = then,
        Else = otherwise,
    };

    private static PartitionStep Partition() => new() { Id = Guid.NewGuid(), Name = "Partition" };

    private static ApplyImageStep Apply(Guid imageId) => new() { Id = Guid.NewGuid(), Name = "Apply", ImageId = imageId };

    private static RunScriptStep Script(SequencePhase phase, Guid? packageId = null) =>
        new() { Id = Guid.NewGuid(), Name = "Script", Phase = phase, Script = "hostname", PackageId = packageId };

    [Fact]
    public void ChecksTheImagesAndPackagesOfEveryBranch()
    {
        ApplyImageStep gone = Apply(Guid.NewGuid());
        ApplyImageStep raw = Apply(s_raw.Id);
        RunScriptStep drivers = Script(SequencePhase.WindowsPE, s_drivers.Id);
        RunScriptStep missing = Script(SequencePhase.WindowsPE, Guid.NewGuid());
        GroupStep group = new() { Id = Guid.NewGuid(), Name = "Scripts", Steps = [drivers, missing] };

        SequenceValidation validation = SequenceChecks.Check(
            Definition(Partition(), If([gone], [If([raw], [Apply(s_windows.Id)])]), group),
            References());

        Assert.Equal(
            [
                (gone.Id, "imageId", "sequence.imageGone"),
                (raw.Id, "imageId", "sequence.imageIsRaw"),
                (drivers.Id, "packageId", "sequence.packageIsDrivers"),
                (missing.Id, "packageId", "sequence.packageGone"),
            ],
            validation.Problems.Select(problem => (problem.StepId!.Value, problem.Field!, problem.Code!)));
    }

    [Fact]
    public void WarnsOfNoLocalAdministratorWhenSomePathGoesOnInWindows()
    {
        WriteUnattendStep plain = new() { Id = Guid.NewGuid(), Name = "Answer file" };
        WriteUnattendStep withAdministrator = plain with { Id = Guid.NewGuid(), LocalAdministrator = true };
        SequenceDefinition onePath = Definition(
            Partition(),
            Apply(s_windows.Id),
            If([plain, Script(SequencePhase.Windows)], [plain with { Id = Guid.NewGuid() }]));

        SequenceProblem warning = Assert.Single(SequenceChecks.Check(onePath, References()).Warnings);
        Assert.Equal(ServerMessages.SequenceNoAdministratorWarning.Code, warning.Code);

        Assert.Empty(SequenceChecks.Check(
            Definition(Partition(), Apply(s_windows.Id), If([withAdministrator, Script(SequencePhase.Windows)], [])),
            References()).Warnings);
        Assert.Empty(SequenceChecks.Check(Definition(Partition(), Apply(s_windows.Id), If([plain], [])), References()).Warnings);
    }

    // The validator's own warnings come first.
    [Fact]
    public void KeepsTheValidatorsWarnings()
    {
        GroupStep empty = new() { Id = Guid.NewGuid(), Name = "Later" };

        SequenceProblem warning = Assert.Single(SequenceChecks.Check(Definition(Partition(), empty), References()).Warnings);

        Assert.Equal((empty.Id, "sequence.emptyContainerWarning"), (warning.StepId!.Value, warning.Code!));
    }

    [Fact]
    public void NeedsAComputerNameForAJoinOnAnyBranchOrADeclaredComputerName()
    {
        JoinDomainStep join = new() { Id = Guid.NewGuid(), Name = "Join" };
        SequenceDefinition plain = Definition(Partition(), Apply(s_windows.Id));

        Assert.Null(SequenceChecks.ComputerNameUse(plain));
        Assert.Equal(
            ServerMessages.DeploymentJoinsDomainUnderName.Code,
            SequenceChecks.ComputerNameUse(Definition(Partition(), Apply(s_windows.Id), If([], [join])))?.Code);
        Assert.Equal(
            ServerMessages.SequenceNamesMachineWithValue.Code,
            SequenceChecks.ComputerNameUse(plain with
            {
                Variables = [new VariableDeclaration { Name = "computername", Default = "PC-{{SerialNumber|alnum|right:12}}" }],
            })?.Code);
    }

    [Fact]
    public void FindsARawImageInsideAGroup()
    {
        GroupStep group = new() { Id = Guid.NewGuid(), Name = "Disk", Steps = [new WriteRawImageStep { Id = Guid.NewGuid(), Name = "Write", ImageId = s_raw.Id }] };

        Assert.Same(s_raw, SequenceChecks.RawImage(Definition(group), References()));
        Assert.Null(SequenceChecks.RawImage(Definition(Partition()), References()));
    }

    // A template's values are known only when the run takes them, so only a setting written out is checked here.
    [Fact]
    public void ChecksOnlyTheAnswerFileSettingsWrittenOut()
    {
        WriteUnattendStep templated = new()
        {
            Id = Guid.NewGuid(),
            Name = "Answer file",
            TimeZone = "{{TimeZone}}",
            Locale = "{{Locale}}",
            Keyboard = "{{Keyboard}}",
            LocalAdministrator = true,
        };
        JoinDomainStep join = new() { Id = Guid.NewGuid(), Name = "Join", OrganizationalUnit = "OU={{Office}},DC=corp,DC=example" };
        SequenceDefinition definition = Definition(Partition(), Apply(s_windows.Id), templated, join) with
        {
            Variables = [new VariableDeclaration { Name = "Office", Default = "Vienna" }],
        };

        Assert.Empty(SequenceChecks.Check(definition, References()).Problems);

        WriteUnattendStep written = templated with { TimeZone = "Middle Earth Standard Time" };
        SequenceProblem problem = Assert.Single(SequenceChecks.Check(definition with { Steps = [Partition(), Apply(s_windows.Id), written] }, References()).Problems);
        Assert.Equal((written.Id, "timeZone"), (problem.StepId!.Value, problem.Field!));
    }
}

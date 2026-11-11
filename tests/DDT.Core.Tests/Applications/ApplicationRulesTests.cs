// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Applications;
using DDT.Contracts.Messages;
using DDT.Core.Applications;
using Xunit;

namespace DDT.Core.Tests.Applications;

public sealed class ApplicationRulesTests
{
    private static readonly Guid s_self = Guid.Parse("0197a3c0-0000-7000-8000-000000000001");
    private static readonly Guid s_other = Guid.Parse("0197a3c0-0000-7000-8000-000000000002");
    private static readonly Guid s_package = Guid.Parse("0197a3c0-0000-7000-8000-000000000003");

    internal static SaveApplicationRequest Command { get; } = new(
        Revision: 0,
        Name: "7-Zip",
        Description: null,
        Publisher: "Igor Pavlov",
        Version: "24.09",
        Kind: ApplicationKind.Command,
        PackageId: s_package,
        Command: "msiexec /i 7z2409-x64.msi /qn /norestart",
        SuccessExitCodes: [0],
        RestartExitCodes: [3010],
        Detection: null,
        DependsOn: [],
        WingetId: null,
        WingetVersion: null,
        TimeoutMinutes: ApplicationLimits.DefaultTimeoutMinutes,
        ContinueOnFailure: false);

    [Fact]
    public void AcceptsACommandWithFiles() => Assert.Empty(ApplicationRules.Check(Command, s_self));

    [Fact]
    public void AcceptsACommandWithoutFiles() => Assert.Empty(ApplicationRules.Check(Command with { PackageId = null }, null));

    [Fact]
    public void AcceptsAnMsixPackage() =>
        Assert.Empty(ApplicationRules.Check(Command with { Kind = ApplicationKind.Msix, Command = null }, null));

    [Fact]
    public void AcceptsAWingetPackage() =>
        Assert.Empty(ApplicationRules.Check(
            Command with { Kind = ApplicationKind.Winget, PackageId = null, Command = null, WingetId = "Mozilla.Firefox", WingetVersion = "132.0.1" },
            null));

    [Fact]
    public void RefusesAnUnknownKind() =>
        AssertProblem(Command with { Kind = (ApplicationKind)7 }, "kind", ServerMessages.ApplicationKindChoose);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void ACommandNeedsACommand(string? command) =>
        AssertProblem(Command with { Command = command }, "command", ServerMessages.ApplicationCommandRequired);

    [Theory]
    [InlineData("setup.exe /S\r\ndel C:\\x")]
    [InlineData("setup.exe /S\0")]
    public void RefusesACommandOverMoreThanOneLine(string command) =>
        AssertProblem(Command with { Command = command }, "command", ServerMessages.ApplicationCommandLength);

    [Fact]
    public void RefusesACommandOverTheLimit() =>
        AssertProblem(Command with { Command = new string('a', ApplicationLimits.MaxCommandLength + 1) }, "command", ServerMessages.ApplicationCommandLength);

    [Fact]
    public void AnMsixPackageNeedsFilesAndNoCommand()
    {
        SaveApplicationRequest msix = Command with { Kind = ApplicationKind.Msix, PackageId = null };

        AssertProblems(
            msix,
            ("command", ServerMessages.ApplicationCommandNotUsed),
            ("packageId", ServerMessages.ApplicationFilesRequired));
    }

    [Fact]
    public void AWingetPackageNeedsAnIdAndNothingElse()
    {
        SaveApplicationRequest winget = Command with { Kind = ApplicationKind.Winget };

        AssertProblems(
            winget,
            ("command", ServerMessages.ApplicationCommandNotUsed),
            ("packageId", ServerMessages.ApplicationFilesNotUsed),
            ("wingetId", ServerMessages.ApplicationWingetId));
    }

    [Theory]
    [InlineData("Mozilla.Firefox")]
    [InlineData("Microsoft.VisualStudio.2022.Community")]
    [InlineData("9NBLGGH4NNS1")]
    [InlineData("Git.Git")]
    [InlineData("Notepad++.Notepad++")]
    public void AcceptsWingetIds(string id) => Assert.True(ApplicationRules.IsWingetId(id));

    [Theory]
    [InlineData("")]
    [InlineData("Mozilla Firefox")]
    [InlineData("Mozilla..Firefox")]
    [InlineData(".Firefox")]
    [InlineData("Mozilla.")]
    [InlineData("Mozilla/Firefox")]
    [InlineData("C:Firefox")]
    [InlineData("a.b.c.d.e.f.g.h.i")]
    [InlineData("Mozilla.FirefoxFirefoxFirefoxFirefoxFirefox")]
    public void RefusesWhatIsNoWingetId(string id) => Assert.False(ApplicationRules.IsWingetId(id));

    [Fact]
    public void RefusesAWingetVersionWithSpaces() =>
        AssertProblem(
            Command with { Kind = ApplicationKind.Winget, PackageId = null, Command = null, WingetId = "Git.Git", WingetVersion = "2.47 beta" },
            "wingetVersion",
            ServerMessages.ApplicationWingetVersion);

    [Fact]
    public void OnlyAWingetPackageHasAWingetId() =>
        AssertProblem(Command with { WingetVersion = "1.0" }, "wingetId", ServerMessages.ApplicationWingetNotUsed);

    [Fact]
    public void NeedsOneSuccessCodeWithoutRestartCodes() =>
        AssertProblem(Command with { SuccessExitCodes = [], RestartExitCodes = [] }, "successExitCodes", ServerMessages.SequenceSuccessExitCodes);

    [Fact]
    public void TakesNoSuccessCodeWhenEveryEndIsARestart() =>
        Assert.Empty(ApplicationRules.Check(Command with { SuccessExitCodes = [], RestartExitCodes = [0, 3010] }, null));

    [Fact]
    public void RefusesTooManySuccessCodes() =>
        AssertProblem(Command with { SuccessExitCodes = [.. Enumerable.Range(1, 17)], RestartExitCodes = [0] }, "successExitCodes", ServerMessages.SequenceSuccessExitCodes);

    [Fact]
    public void RefusesTooManyRestartCodes() =>
        AssertProblem(Command with { RestartExitCodes = [.. Enumerable.Range(1, 17)] }, "restartExitCodes", ServerMessages.SequenceRebootExitCodes);

    [Fact]
    public void RefusesACodeThatMeansSuccessAndRestart()
    {
        IReadOnlyList<ApplicationProblem> problems = ApplicationRules.Check(Command with { SuccessExitCodes = [0, 3010, 1641], RestartExitCodes = [3010, 1641] }, null);

        ApplicationProblem problem = Assert.Single(problems);
        Assert.Equal("restartExitCodes", problem.Field);
        Assert.Equal("An exit code cannot mean both success and a restart: 3010, 1641.", problem.Message.Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void RefusesATimeoutOutsideOneMinuteToADay(int minutes) =>
        AssertProblem(Command with { TimeoutMinutes = minutes }, "timeoutMinutes", ServerMessages.SequenceScriptTimeout);

    [Fact]
    public void RefusesADependencyOnItself() =>
        AssertProblem(Command with { DependsOn = [s_other, s_self] }, "dependsOn[1]", ServerMessages.ApplicationDependsOnItself);

    [Fact]
    public void RefusesADependencyListedTwice() =>
        AssertProblem(Command with { DependsOn = [s_other, s_other] }, "dependsOn[1]", ServerMessages.ApplicationListedTwice);

    [Fact]
    public void RefusesAnEmptyDependency() =>
        AssertProblem(Command with { DependsOn = [Guid.Empty] }, "dependsOn[0]", ServerMessages.ApplicationGone);

    [Fact]
    public void RefusesTooManyDependencies() =>
        AssertProblem(
            Command with { DependsOn = [.. Enumerable.Range(0, ApplicationLimits.MaxDependencies + 1).Select(_ => Guid.NewGuid())] },
            "dependsOn",
            ServerMessages.ApplicationTooManyDependencies);

    [Fact]
    public void ChecksNameDescriptionPublisherAndVersion()
    {
        SaveApplicationRequest request = Command with
        {
            Name = "A;B",
            Description = new string('d', ApplicationLimits.MaxDescriptionLength + 1),
            Publisher = new string('p', ApplicationLimits.MaxPublisherLength + 1),
            Version = "1.0\n",
        };

        AssertProblems(
            request,
            ("name", ServerMessages.ApplicationNameSemicolon),
            ("description", ServerMessages.DescriptionLength),
            ("publisher", ServerMessages.ApplicationPublisherLength),
            ("version", ServerMessages.ApplicationVersionLength));
    }

    // A request from outside can leave out lists the record declares.
    [Fact]
    public void SaysWhatIsMissingInsteadOfThrowing()
    {
        SaveApplicationRequest request = JsonSerializer.Deserialize(
            """{"kind":"Command","command":"setup.exe /S","timeoutMinutes":60}""",
            DdtJsonContext.Default.SaveApplicationRequest) ?? throw new InvalidOperationException("The request did not read.");

        AssertProblems(
            request,
            ("name", ServerMessages.NameLength),
            ("successExitCodes", ServerMessages.SequenceSuccessExitCodes),
            ("restartExitCodes", ServerMessages.SequenceRebootExitCodes));
    }

    internal static void AssertProblem(SaveApplicationRequest request, string field, MessageTemplate message) =>
        AssertProblems(request, (field, message));

    private static void AssertProblems(SaveApplicationRequest request, params (string Field, MessageTemplate Message)[] expected) =>
        Assert.Equal(
            expected.Select(problem => (problem.Field, problem.Message.Code)),
            ApplicationRules.Check(request, s_self).Select(problem => (problem.Field, problem.Message.Code)));
}

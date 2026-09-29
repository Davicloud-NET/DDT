// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Unattend;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Xunit;

namespace DDT.Server.Tests;

public sealed class UnattendRendererTests
{
    private static readonly WriteUnattendStep s_step = new() { Id = Guid.NewGuid(), Name = "Answer file", LocalAdministrator = true };

    private static UnattendSettings Settings(DeploymentOptions options, string? assignedName, string? imageLanguage, WriteUnattendStep? step = null)
    {
        RunInputs inputs = RunInputs.Capture(
            new Machine { SmbiosUuid = "uuid", PrimaryMac = "020000000001", AssignedName = assignedName },
            options,
            DateTimeOffset.UtcNow);

        return new UnattendRenderer().Settings(inputs, step ?? s_step, imageLanguage, options.LocalAdministrator.Password);
    }

    [Fact]
    public void WithoutSettingsFollowsTheImageAndLetsWindowsChooseTheRest()
    {
        UnattendSettings settings = Settings(new DeploymentOptions(), null, "de-DE");

        Assert.Equal("amd64", settings.ProcessorArchitecture);
        Assert.Equal("*", settings.ComputerName);
        Assert.Null(settings.TimeZone);
        Assert.Equal("de-DE", settings.UiLanguage);
        Assert.Equal("de-DE", settings.Locale);
        Assert.Equal("de-DE", settings.Keyboard);
        Assert.Null(settings.LocalAdministrator);
    }

    [Fact]
    public void AnImageWithoutALanguageGetsEnglish()
    {
        UnattendSettings settings = Settings(new DeploymentOptions(), "PC-0001", null);

        Assert.Equal("PC-0001", settings.ComputerName);
        Assert.Equal("en-US", settings.UiLanguage);
        Assert.Equal("en-US", settings.Locale);
        Assert.Equal("en-US", settings.Keyboard);
    }

    [Fact]
    public void TheSettingsOverrideTheImageExceptForTheDisplayLanguage()
    {
        DeploymentOptions options = new()
        {
            TimeZone = "W. Europe Standard Time",
            Locale = "de-CH",
            Keyboard = "0807:00000807",
            LocalAdministrator = new LocalAdministratorOptions { Name = " Support ", Password = "Local password 7" },
        };

        UnattendSettings settings = Settings(options, "PC-0001", "en-US");

        Assert.Equal("W. Europe Standard Time", settings.TimeZone);
        Assert.Equal("en-US", settings.UiLanguage);
        Assert.Equal("de-CH", settings.Locale);
        Assert.Equal("0807:00000807", settings.Keyboard);
        Assert.Equal(new LocalAdministrator("Support", "Local password 7"), settings.LocalAdministrator);
    }

    [Fact]
    public void TheStepOverridesTheSettingsAndDecidesOnTheAdministrator()
    {
        DeploymentOptions options = new()
        {
            TimeZone = "W. Europe Standard Time",
            Locale = "de-CH",
            LocalAdministrator = new LocalAdministratorOptions { Password = "Local password 7" },
        };
        WriteUnattendStep step = new() { Id = Guid.NewGuid(), Name = "Answer file", TimeZone = "UTC", Locale = "fr-CH", Keyboard = "100c:0000100c" };

        UnattendSettings settings = Settings(options, null, "en-US", step);

        Assert.Equal("UTC", settings.TimeZone);
        Assert.Equal("fr-CH", settings.Locale);
        Assert.Equal("100c:0000100c", settings.Keyboard);
        Assert.Null(settings.LocalAdministrator);
    }

    // The agent fetches the answer file after steps may have changed the run's variables. So its templates and the
    // settings the run started with are worked out from them at that point, and checked like a sequence's settings.
    [Fact]
    public void TheStepsTemplatesAndTheRunsSettingsComeFromTheValuesWhenTheFileIsMade()
    {
        RunInputs inputs = RunInputs.Capture(
            new Machine { SmbiosUuid = "uuid", PrimaryMac = "020000000001", AssignedName = "PC-0001" },
            new DeploymentOptions { TimeZone = "UTC", Locale = "de-AT" },
            DateTimeOffset.UtcNow);
        WriteUnattendStep step = new() { Id = Guid.NewGuid(), Name = "Answer file", TimeZone = "{{Zone}}", Keyboard = "{{Layout|lower}}" };
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Zone"] = "W. Europe Standard Time",
            ["Layout"] = "0407:00000407",
            ["ComputerName"] = "PC-0002",
            ["Locale"] = "fr-FR",
        };

        (UnattendSettings? settings, string? problem) = new UnattendRenderer().Settings(inputs, step, "en-US", null, name => values.GetValueOrDefault(name));

        Assert.Null(problem);
        Assert.Equal(
            ("PC-0002", "W. Europe Standard Time", "fr-FR", "0407:00000407"),
            (settings!.ComputerName, settings.TimeZone, settings.Locale, settings.Keyboard));

        // Without values, the run's own settings stand, like for a run that started before runs had values.
        (UnattendSettings? plain, _) = new UnattendRenderer().Settings(inputs, step with { TimeZone = null, Keyboard = null }, "en-US", null, _ => null);
        Assert.Equal(("PC-0001", "UTC", "de-AT"), (plain!.ComputerName, plain.TimeZone, plain.Locale));
    }

    [Theory]
    [InlineData("{{Missing}}", null, "The answer file's time zone, {{Missing}}, cannot be worked out from the run's values.")]
    [InlineData("{{Zone}}", null, "The answer file's time zone, Mars Standard Time, is not a Windows time zone.")]
    [InlineData(null, "PC 0002", "The computer name PC 0002 cannot be used in the answer file.")]
    public void RefusesWhatTheValuesMakeThatWindowsWouldNotTake(string? timeZone, string? computerName, string expected)
    {
        RunInputs inputs = RunInputs.Capture(new Machine { SmbiosUuid = "uuid", PrimaryMac = "020000000001" }, new DeploymentOptions(), DateTimeOffset.UtcNow);
        WriteUnattendStep step = new() { Id = Guid.NewGuid(), Name = "Answer file", TimeZone = timeZone };
        Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase) { ["Zone"] = "Mars Standard Time", ["ComputerName"] = computerName };

        (string? answerFile, string? problem) = new UnattendRenderer().Render(inputs, step, "en-US", null, name => values.GetValueOrDefault(name));

        Assert.Null(answerFile);
        Assert.StartsWith(expected, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AKeyboardDefaultsToTheConfiguredLocale()
    {
        UnattendSettings settings = Settings(new DeploymentOptions { Locale = "fr-FR" }, null, "de-DE");

        Assert.Equal("fr-FR", settings.Keyboard);
    }
}

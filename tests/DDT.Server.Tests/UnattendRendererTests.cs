// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Unattend;
using DDT.Server.Deployments;
using Microsoft.Extensions.Options;
using Xunit;

namespace DDT.Server.Tests;

public sealed class UnattendRendererTests
{
    private static UnattendSettings Settings(DeploymentOptions options, string? assignedName, string? imageLanguage) =>
        new UnattendRenderer(Options.Create(options)).Settings(assignedName, imageLanguage);

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
            LocalAdministrator = new LocalAdministratorOptions { Name = "Support", Password = "Local password 7" },
            Domain = new DomainOptions
            {
                Name = "corp.example",
                OrganizationalUnit = "OU=Workstations,DC=corp,DC=example",
                UserName = "ddt-join@corp.example",
                Password = "Join password 7",
            },
        };

        UnattendSettings settings = Settings(options, "PC-0001", "en-US");

        Assert.Equal("W. Europe Standard Time", settings.TimeZone);
        Assert.Equal("en-US", settings.UiLanguage);
        Assert.Equal("de-CH", settings.Locale);
        Assert.Equal("0807:00000807", settings.Keyboard);
        Assert.Equal(new LocalAdministrator("Support", "Local password 7"), settings.LocalAdministrator);
    }

    [Fact]
    public void AKeyboardDefaultsToTheConfiguredLocale()
    {
        UnattendSettings settings = Settings(new DeploymentOptions { Locale = "fr-FR" }, null, "de-DE");

        Assert.Equal("fr-FR", settings.Keyboard);
    }
}

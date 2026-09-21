// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Configuration;
using DDT.Server.Deployments;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DeploymentOptionsValidationTests
{
    private static DeploymentOptions Domain(
        string? userName = @"CORP\ddt-join",
        string? password = "Join password 7",
        string? administratorPassword = "Local password 7",
        string? organizationalUnit = null) => new()
        {
            LocalAdministrator = new LocalAdministratorOptions { Password = administratorPassword },
            Domain = new DomainOptions
            {
                Name = "corp.example",
                UserName = userName,
                Password = password,
                OrganizationalUnit = organizationalUnit,
            },
        };

    [Fact]
    public void AcceptsNothingConfigured()
    {
        Assert.Empty(DeploymentOptionsValidation.FindProblems(new DeploymentOptions()));
    }

    [Fact]
    public void AcceptsACompleteDomainJoin()
    {
        Assert.Empty(DeploymentOptionsValidation.FindProblems(Domain(organizationalUnit: "OU=Workstations,DC=corp,DC=example")));
    }

    [Theory]
    [InlineData("W. Europe Standard Time", true)]
    [InlineData("UTC", true)]
    [InlineData("Europe/Berlin", false)]
    [InlineData("Mars Standard Time", false)]
    public void AcceptsOnlyWindowsTimeZoneIds(string timeZone, bool valid)
    {
        IReadOnlyList<SettingProblem> problems = DeploymentOptionsValidation.FindProblems(new DeploymentOptions { TimeZone = timeZone });

        Assert.Equal(valid, problems.Count == 0);
        Assert.All(problems, problem => Assert.Equal("TimeZone", problem.Field));
    }

    [Fact]
    public void ADomainNeedsItsAccountAndALocalAdministrator()
    {
        IReadOnlyList<SettingProblem> problems =
            DeploymentOptionsValidation.FindProblems(Domain(userName: null, password: null, administratorPassword: null));

        Assert.Equal(["Domain:UserName", "Domain:Password", "LocalAdministrator:Password"], problems.Select(problem => problem.Field));
    }

    [Theory]
    [InlineData(@"CORP\ddt-join", true)]
    [InlineData("ddt-join@corp.example", true)]
    [InlineData("ddt-join", false)]
    [InlineData(@"\ddt-join", false)]
    [InlineData(@"CORP\ddt\join", false)]
    [InlineData("ddt@join@corp.example", false)]
    [InlineData(@"CORP\ddt@corp.example", false)]
    public void TheJoinAccountIsQualified(string userName, bool valid)
    {
        Assert.Equal(valid, DeploymentOptionsValidation.FindProblems(Domain(userName: userName)).Count == 0);
    }

    [Theory]
    [InlineData("OU=Workstations,DC=corp,DC=example", true)]
    [InlineData("CN=Computers,DC=corp,DC=example", true)]
    [InlineData("ou=Workstations,dc=corp,dc=example", true)]
    [InlineData("LDAP://OU=Workstations,DC=corp,DC=example", false)]
    [InlineData("Workstations", false)]
    [InlineData("OU=Workstations", false)]
    public void TheOrganizationalUnitIsADistinguishedName(string organizationalUnit, bool valid)
    {
        Assert.Equal(valid, DeploymentOptionsValidation.FindProblems(Domain(organizationalUnit: organizationalUnit)).Count == 0);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Local Admin", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU", false)]
    [InlineData("Ad*min", false)]
    [InlineData("Admin<1>", false)]
    public void TheLocalAdministratorNameIsAnAccountName(string name, bool valid)
    {
        DeploymentOptions options = new() { LocalAdministrator = new LocalAdministratorOptions { Name = name, Password = "Local password 7" } };

        Assert.Equal(valid, DeploymentOptionsValidation.FindProblems(options).Count == 0);
    }

    [Fact]
    public void ListsEveryProblemByItsField()
    {
        DeploymentOptions options = new()
        {
            TimeZone = "Europe/Berlin",
            Domain = new DomainOptions { Name = "corp.example", OrganizationalUnit = "LDAP://OU=x,DC=corp" },
        };

        Assert.Equal(
            ["TimeZone", "Domain:UserName", "Domain:Password", "LocalAdministrator:Password", "Domain:OrganizationalUnit"],
            DeploymentOptionsValidation.FindProblems(options).Select(problem => problem.Field));
    }

    [Fact]
    public void TheServerDoesNotStartWithInvalidSettings()
    {
        using SettingsApplication application = new(
            ("DDT:Deployment:TimeZone", "Europe/Berlin"),
            ("DDT:Machines:ZeroTouchNetworks", "10.20.0.0/16"));

        Exception refusal = Assert.ThrowsAny<Exception>(() => application.CreateClient());

        Assert.Contains("DDT:Deployment:TimeZone", MessagesOf(refusal), StringComparison.Ordinal);
    }

    [Fact]
    public void TheServerDoesNotStartWithAnInvalidZeroTouchNetwork()
    {
        using SettingsApplication application = new(("DDT:Machines:ZeroTouchNetworks", "10.20.0.0/16, 10.30.0.1/16"));

        Exception refusal = Assert.ThrowsAny<Exception>(() => application.CreateClient());

        Assert.Contains("'10.30.0.1/16'", MessagesOf(refusal), StringComparison.Ordinal);
    }

    private static string MessagesOf(Exception exception) =>
        exception.InnerException is null ? exception.Message : exception.Message + Environment.NewLine + MessagesOf(exception.InnerException);
}

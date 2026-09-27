// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Configuration;
using DDT.Server.Authentication;
using DDT.Server.Ldap;
using Xunit;

namespace DDT.Server.Tests;

public sealed class LdapOptionsValidationTests
{
    [Fact]
    public void AMapToRolesDdtHasIsValid()
    {
        LdapOptions options = new();
        options.GroupRoleMap["CN=DDT Operators,OU=Groups,DC=corp,DC=example"] = "operator";

        Assert.Empty(LdapOptionsValidation.FindProblems(options));
        Assert.Equal("operator", options.GroupRoleMap["cn=ddt operators,ou=groups,dc=corp,dc=example"]);
    }

    [Fact]
    public void AMapToARoleDdtDoesNotHaveIsNot()
    {
        LdapOptions options = new();
        options.GroupRoleMap["admins"] = "Owner";

        SettingProblem problem = Assert.Single(LdapOptionsValidation.FindProblems(options));
        Assert.Equal("GroupRoleMap:admins", problem.Field);
        Assert.Equal("'Owner' is not a DDT role. Use Administrator, Operator, Viewer.", problem.Message);
    }

    // No groups are read without nested groups, so everyone would be refused.
    [Fact]
    public void AMapNeedsTheGroupsToBeRead()
    {
        LdapOptions options = new() { ResolveNestedGroups = false };

        Assert.Empty(LdapOptionsValidation.FindProblems(options));

        options.GroupRoleMap["admins"] = DdtRoleNames.Administrator;

        Assert.Equal(nameof(LdapOptions.ResolveNestedGroups), Assert.Single(LdapOptionsValidation.FindProblems(options)).Field);
    }

    [Fact]
    public void AnUnknownRoleInTheMapStopsTheServer()
    {
        using SettingsApplication misconfigured = new(("DDT:Ldap:GroupRoleMap:admins", "Owner"));

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => misconfigured.CreateClient());

        Assert.Contains("DDT:Ldap:GroupRoleMap:admins: 'Owner' is not a DDT role.", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CN=DDT Operators,OU=Groups,DC=corp,DC=example", "DDT Operators")]
    [InlineData(@"CN=Smith\, Jane,OU=People,DC=corp,DC=example", "Smith, Jane")]
    [InlineData("operators", "operators")]
    public void NamesAGroupByTheFirstPartOfItsName(string distinguishedName, string expected)
    {
        Assert.Equal(expected, DistinguishedNames.FirstValue(distinguishedName));
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Host.Startup;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ConfigurationKeyCheckTests
{
    // One per section. Read as written, each would leave its setting at the default without a word.
    [Theory]
    [InlineData("DDT:RequireHttp", "false")]
    [InlineData("DDT:Https:GenerateSelfSignedCertificates", "false")]
    [InlineData("DDT:Deployment:Domain:Nmae", "corp.example")]
    [InlineData("DDT:Machines:RequireWebAproval", "true")]
    [InlineData("DDT:Ldap:BindPasword", "directory password")]
    [InlineData("DDT:Oidc:AutoProvison", "true")]
    [InlineData("DDT:ForwardedHeaders:KnownProxy", "10.10.0.5")]
    [InlineData("DDT:Pxe:Interface", "eth0")]
    [InlineData("DDT:Pxe:BootTargets:X64Uefi:BootFiel", "x64/bootmgfw.efi")]
    [InlineData("DDT:Agent:BinaryPth", "/srv/ddt-agent.exe")]
    public void AMisspelledKeyStopsTheServer(string key, string value)
    {
        using SettingsApplication application = new((key, value));

        Exception refusal = Assert.ThrowsAny<Exception>(() => application.CreateClient());

        Assert.Contains(key[(key.LastIndexOf(':') + 1)..], MessagesOf(refusal), StringComparison.Ordinal);
    }

    [Fact]
    public void KeysAreMatchedWithoutRegardToCaseAndMapKeysAreFree()
    {
        IConfiguration configuration = Configuration(
            ("ddt:roles", "web"),
            ("DDT:Https:SubjectAlternativeNames", "ddt.corp.example"),
            ("DDT:Machines:requirewebapproval", "true"),
            ("DDT:Ldap:GroupRoleMap:CN=DDT Operators,OU=Groups,DC=corp,DC=example", "Operator"),
            ("DDT:Oidc:Scopes:3", "groups"),
            ("DDT:Pxe:BootTargets:X64Uefi:Method", "Tftp"),
            ("DDT:Pxe:BootTargets:X64Uefi:BootFile", "x64/bootmgfw.efi"));

        Assert.Empty(DdtConfigurationCheck.FindUnknownKeys(configuration));
    }

    [Fact]
    public void ReportsEveryUnknownKeyAtOnce()
    {
        IConfiguration configuration = Configuration(
            ("DDT:Rolse", "web"),
            ("DDT:Machines:MaxWaitng", "10"),
            ("DDT:Deployment:LocalAdministrator:Nmae", "Admin"));

        IReadOnlyList<string> problems = DdtConfigurationCheck.FindUnknownKeys(configuration);

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, problem => problem.StartsWith("DDT:Rolse is not a setting DDT reads.", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("DDT:Machines could not be read.", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("DDT:Deployment could not be read.", StringComparison.Ordinal));
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

    private static string MessagesOf(Exception exception) =>
        exception.InnerException is null ? exception.Message : exception.Message + Environment.NewLine + MessagesOf(exception.InnerException);
}

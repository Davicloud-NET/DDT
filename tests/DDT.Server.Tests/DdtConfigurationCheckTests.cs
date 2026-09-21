// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Host.Startup;
using DDT.Server.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DdtConfigurationCheckTests
{
    private static readonly IReadOnlySet<DeploymentRole> s_webAndPxe = new HashSet<DeploymentRole> { DeploymentRole.Web, DeploymentRole.Pxe };

    private static readonly DdtOptions s_store = new() { StorePath = Path.Combine(Path.GetTempPath(), "ddt-check-store") };

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

        Assert.Empty(DdtConfigurationCheck.FindProblems(configuration, s_store, s_webAndPxe));
    }

    [Fact]
    public void ReportsEveryUnknownKeyAtOnce()
    {
        IConfiguration configuration = Configuration(
            ("DDT:Rolse", "web"),
            ("DDT:Machines:MaxWaitng", "10"),
            ("DDT:Deployment:LocalAdministrator:Nmae", "Admin"));

        IReadOnlyList<string> problems = DdtConfigurationCheck.FindProblems(configuration, s_store, s_webAndPxe);

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, problem => problem.StartsWith("DDT:Rolse is not a setting DDT reads.", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("DDT:Machines could not be read.", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.StartsWith("DDT:Deployment could not be read.", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsEveryInvalidValueAtOnceByItsKey()
    {
        IConfiguration configuration = Configuration(
            ("DDT:Deployment:TimeZone", "Europe/Berlin"),
            ("DDT:Machines:ZeroTouchNetworks", "10.30.0.1/16"),
            ("DDT:ForwardedHeaders:KnownProxies", "proxy.corp.example"),
            ("DDT:Pxe:TftpMaxWindowSize", "65"));

        IReadOnlyList<string> problems = DdtConfigurationCheck.FindProblems(configuration, s_store, s_webAndPxe);

        Assert.Equal(4, problems.Count);
        Assert.StartsWith("DDT:Deployment:TimeZone: 'Europe/Berlin'", problems[0], StringComparison.Ordinal);
        Assert.StartsWith("DDT:Machines:ZeroTouchNetworks: '10.30.0.1/16'", problems[1], StringComparison.Ordinal);
        Assert.StartsWith("DDT:ForwardedHeaders:KnownProxies: 'proxy.corp.example'", problems[2], StringComparison.Ordinal);
        Assert.StartsWith("DDT:Pxe:TftpMaxWindowSize: ", problems[3], StringComparison.Ordinal);
    }

    [Fact]
    public void ChecksNetbootValuesOnlyWhereThePxeRoleRuns()
    {
        IConfiguration configuration = Configuration(("DDT:Pxe:TftpMaxWindowSize", "65"));

        Assert.Empty(DdtConfigurationCheck.FindProblems(configuration, s_store, DeploymentRoles.Default));
    }

    [Fact]
    public void RefusesABootDirectoryThatWouldServeTheStoreOrTheTlsKey()
    {
        IConfiguration configuration = Configuration(
            ("DDT:Pxe:BootDirectory", s_store.StorePath),
            ("Kestrel:Certificates:Default:KeyPath", Path.Combine(s_store.StorePath, "certs", "ddt-key.pem")));

        IReadOnlyList<string> problems = DdtConfigurationCheck.FindProblems(configuration, s_store, s_webAndPxe);

        Assert.Equal(2, problems.Count);
        Assert.All(problems, problem => Assert.StartsWith($"DDT:Pxe:BootDirectory: '{s_store.StorePath}' holds ", problem, StringComparison.Ordinal));
    }

    // A PFX needs no KeyPath, and an endpoint can carry a certificate of its own.
    [Theory]
    [InlineData("Kestrel:Certificates:Default:Path")]
    [InlineData("Kestrel:Endpoints:Https:Certificate:KeyPath")]
    public void RefusesABootDirectoryThatHoldsAnyCertificateFile(string key)
    {
        string boot = Path.Combine(Path.GetTempPath(), "ddt-check-boot");
        IConfiguration configuration = Configuration(
            ("DDT:Pxe:BootDirectory", boot),
            (key, Path.Combine(boot, "certs", "ddt.pfx")),
            ("Kestrel:Certificates:Default:Password", "secret"));

        string problem = Assert.Single(DdtConfigurationCheck.FindProblems(configuration, s_store, s_webAndPxe));

        Assert.StartsWith($"DDT:Pxe:BootDirectory: '{boot}' holds the folder of the TLS certificate or key ", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TheServerNamesEveryProblemWhenItRefusesToStart()
    {
        using SettingsApplication application = new(
            ("DDT:Deployment:TimeZone", "Europe/Berlin"),
            ("DDT:ForwardedHeaders:KnownNetworks", "198.51.100.7/24"));

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => application.CreateClient());

        Assert.StartsWith("The configuration is not valid:", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("DDT:Deployment:TimeZone: ", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("DDT:ForwardedHeaders:KnownNetworks: '198.51.100.7/24'", refusal.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

    private static string MessagesOf(Exception exception) =>
        exception.InnerException is null ? exception.Message : exception.Message + Environment.NewLine + MessagesOf(exception.InnerException);
}

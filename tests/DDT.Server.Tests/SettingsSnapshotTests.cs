// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Nodes;
using DDT.Contracts.Settings;
using DDT.Server.Deployments;
using DDT.Server.Settings;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DDT.Server.Tests;

// What applies: a configured key over the stored value over the code default, and what is safe while a section has
// problems.
public sealed class SettingsSnapshotTests
{
    [Fact]
    public void TheCodeDefaultsApplyWhileNothingIsStoredOrConfigured()
    {
        SettingsSnapshot snapshot = Build([]);

        Assert.Equal("Admin", snapshot.Deployment.LocalAdministrator.Name);
        Assert.False(snapshot.Machines.RequireWebApproval);
        Assert.Equal(100, snapshot.Machines.MaxWaitingPerAddress);
        Assert.Equal(["openid", "profile", "email"], snapshot.Oidc.Scopes);
        Assert.Equal(LogLevel.Information, snapshot.LogLevels[LoggingOptions.DefaultCategory]);
        Assert.All(snapshot.Sections, section => Assert.Equal(0, section.Version));
        Assert.All(snapshot.Sections, section => Assert.Empty(section.Locks));
    }

    [Fact]
    public void AStoredValueAppliesWhileConfigurationHasNoKey()
    {
        SettingsSnapshot snapshot = Build([Stored(SettingsSectionNames.Deployment, """{"timeZone":"W. Europe Standard Time"}""")]);

        Assert.Equal("W. Europe Standard Time", snapshot.Deployment.TimeZone);
        Assert.Equal(3, snapshot[SettingsSectionNames.Deployment].Version);
        Assert.Empty(snapshot[SettingsSectionNames.Deployment].Locks);
    }

    // An empty value locks too, so configuration can force the safe value: here, no zero touch at all.
    [Fact]
    public void AConfiguredKeyLocksItsFieldEvenWhenEmpty()
    {
        SettingsSnapshot snapshot = Build(
            [Stored(SettingsSectionNames.Machines, """{"zeroTouchNetworks":"10.200.0.0/16"}""")],
            ("DDT:Machines:ZeroTouchNetworks", string.Empty));

        SettingLockState locked = Assert.Single(snapshot[SettingsSectionNames.Machines].Locks);
        Assert.Equal("zeroTouchNetworks", locked.Field.Name);
        Assert.Equal("DDT:Machines:ZeroTouchNetworks", locked.ConfigurationKey);
        Assert.Equal("DDT__Machines__ZeroTouchNetworks", SettingsSectionDefinition.EnvironmentVariable(locked.ConfigurationKey));
        Assert.True(locked.StoredDiffers);
        Assert.False(snapshot.Machines.ZeroTouchEnabled);
        Assert.Equal("10.200.0.0/16", snapshot[SettingsSectionNames.Machines].StoredValues["zeroTouchNetworks"]?.GetValue<string>());
    }

    [Fact]
    public void TheStoredValueAppliesAgainOnceTheKeyIsGone()
    {
        StoredSettingsSection stored = Stored(SettingsSectionNames.Machines, """{"maxWaiting":500}""");

        Assert.Equal(200, Build([stored], ("DDT:Machines:MaxWaiting", "200")).Machines.MaxWaiting);
        Assert.Equal(500, Build([stored]).Machines.MaxWaiting);
    }

    [Fact]
    public void AConfiguredValueEqualToTheStoredOneLocksWithoutDiffering()
    {
        SettingsSnapshot snapshot = Build(
            [Stored(SettingsSectionNames.Machines, """{"requireWebApproval":true}""")],
            ("DDT:Machines:RequireWebApproval", "true"));

        Assert.False(Assert.Single(snapshot[SettingsSectionNames.Machines].Locks).StoredDiffers);
    }

    // The binder adds configured entries to a list's defaults; the settings read them on their own, so a list without
    // profile and email removes them.
    [Fact]
    public void AConfiguredCollectionIsReadOnItsOwnAndLocksAsAWhole()
    {
        SettingsSnapshot snapshot = Build(
            [Stored(SettingsSectionNames.Ldap, """{"groupRoleMap":{"CN=Stored,DC=corp":"Viewer"}}""")],
            ("DDT:Oidc:Scopes:0", "openid"),
            ("DDT:Ldap:GroupRoleMap:CN=Configured,DC=corp", "Operator"));

        Assert.Equal(["openid"], snapshot.Oidc.Scopes);
        Assert.Equal("Operator", Assert.Single(snapshot.Ldap.GroupRoleMap).Value);
        Assert.Equal("groupRoleMap", Assert.Single(snapshot[SettingsSectionNames.Ldap].Locks).Field.Name);
        Assert.Equal("Operator", snapshot.Ldap.GroupRoleMap["cn=configured,dc=corp"]);
    }

    [Fact]
    public void AStoredCollectionReplacesTheDefaults()
    {
        SettingsSnapshot snapshot = Build([Stored(SettingsSectionNames.Logging, """{"logLevel":{"Default":"Warning","DDT.Pxe":"Debug"}}""")]);

        Assert.Equal(LogLevel.Warning, snapshot.LogLevels["Default"]);
        Assert.Equal(LogLevel.Debug, snapshot.LogLevels["ddt.pxe"]);
        Assert.False(snapshot.LogLevels.ContainsKey("Microsoft.AspNetCore"));
    }

    // Secrets are values of their own: configured, they lock like any field, and stored, only whether one is set shows.
    [Fact]
    public void ASecretAppliesFromConfigurationOrTheStore()
    {
        StoredSettingsSection stored = Stored(
            SettingsSectionNames.Deployment,
            "{}",
            secrets: new() { ["domain.password"] = new StoredSecret("stored password", false, DateTimeOffset.UnixEpoch, "cipher") });

        SettingsSnapshot fromStore = Build([stored]);
        SettingsSnapshot fromConfiguration = Build([stored], ("DDT:Deployment:Domain:Password", "configured password"));

        Assert.Equal("stored password", fromStore.Deployment.Domain.Password);
        Assert.Equal(new SecretState(true, false, DateTimeOffset.UnixEpoch), fromStore[SettingsSectionNames.Deployment].Secrets["domain.password"]);
        Assert.Equal("configured password", fromConfiguration.Deployment.Domain.Password);
        Assert.True(Assert.Single(fromConfiguration[SettingsSectionNames.Deployment].Locks).StoredDiffers);
        Assert.DoesNotContain("password", fromStore[SettingsSectionNames.Deployment].Values.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASecretThatNoLongerDecryptsClosesItsSection()
    {
        SettingsSnapshot snapshot = Build([Stored(
            SettingsSectionNames.Ldap,
            """{"enabled":true,"host":"dc1.corp.example"}""",
            secrets: new() { ["bindPassword"] = new StoredSecret(null, true, DateTimeOffset.UnixEpoch, "cipher") })]);

        SettingsSectionState ldap = snapshot[SettingsSectionNames.Ldap];

        Assert.Equal(new SecretState(false, true, DateTimeOffset.UnixEpoch), ldap.Secrets["bindPassword"]);
        Assert.Equal("BindPassword", Assert.Single(ldap.Problems).Field);
        Assert.False(snapshot.Ldap.Enabled);
    }

    // A problem caused only by stored values does not stop the server: the section fails closed until the page fixes it.
    [Fact]
    public void AStoredDeploymentWithProblemsRefusesNewRuns()
    {
        SettingsSnapshot snapshot = Build([Stored(SettingsSectionNames.Deployment, """{"domain":{"name":"corp.example"}}""")]);

        Assert.Contains(snapshot.DeploymentProblems, problem => problem.Field == "Domain:UserName");
        Assert.StartsWith("The deployment settings have problems", DeploymentService.SettingsProblem(snapshot)?.Text, StringComparison.Ordinal);
        Assert.Null(DeploymentService.SettingsProblem(Build([])));
    }

    // Web approval stays on when either source asked for it; the caps take their defaults and zero touch is off.
    [Fact]
    public void AStoredMachinesSectionWithProblemsFailsClosed()
    {
        SettingsSnapshot snapshot = Build([Stored(
            SettingsSectionNames.Machines,
            """{"requireWebApproval":true,"maxWaiting":0,"maxWaitingPerAddress":5,"zeroTouchNetworks":"10.200.0.0/16"}""")]);

        Assert.True(snapshot[SettingsSectionNames.Machines].Closed);
        Assert.True(snapshot.Machines.RequireWebApproval);
        Assert.Equal(10_000, snapshot.Machines.MaxWaiting);
        Assert.Equal(100, snapshot.Machines.MaxWaitingPerAddress);
        Assert.True(snapshot.Machines.ZeroTouchNetworks.IsEmpty);
    }

    [Fact]
    public void ClosedSectionsTurnWhatTheyConfigureOff()
    {
        SettingsSnapshot snapshot = Build(
        [
            Stored(SettingsSectionNames.Ldap, """{"enabled":true}"""),
            Stored(SettingsSectionNames.Oidc, """{"enabled":true,"authority":"http://idp.example","clientId":"ddt"}"""),
            Stored(SettingsSectionNames.Pxe, """{"tftpMaxWindowSize":65}"""),
            Stored(SettingsSectionNames.Logging, """{"logLevel":{"Default":"Loud"}}"""),
        ]);

        Assert.False(snapshot.Ldap.Enabled);
        Assert.False(snapshot.Oidc.Enabled);
        Assert.Null(snapshot.Pxe);
        Assert.Equal(LogLevel.Information, snapshot.LogLevels[LoggingOptions.DefaultCategory]);
        Assert.Equal(LogLevel.Warning, snapshot.LogLevels["Microsoft.AspNetCore"]);
        Assert.Equal("LogLevel:Default", Assert.Single(snapshot[SettingsSectionNames.Logging].Problems).Field);
    }

    // With the proxies closed nothing is trusted, and zero touch is off as well: an address could be a proxy's.
    [Fact]
    public void ClosedProxiesTrustNothingAndTurnZeroTouchOff()
    {
        SettingsSnapshot snapshot = Build(
        [
            Stored(SettingsSectionNames.Proxies, """{"knownProxies":"proxy.corp.example"}"""),
            Stored(SettingsSectionNames.Machines, """{"zeroTouchNetworks":"10.200.0.0/16"}"""),
        ]);

        Assert.Equal(ForwardedHeaders.None, snapshot.ForwardedHeaders.ForwardedHeaders);
        Assert.Empty(snapshot.ForwardedHeaders.KnownProxies);
        Assert.Empty(snapshot.ForwardedHeaders.KnownIPNetworks);
        Assert.False(snapshot.Machines.ZeroTouchEnabled);
        Assert.False(snapshot[SettingsSectionNames.Machines].Closed);
    }

    [Fact]
    public void AZeroTouchNetworkThatHoldsAProxyClosesTheMachinesSection()
    {
        SettingsSnapshot snapshot = Build(
        [
            Stored(SettingsSectionNames.Proxies, """{"knownProxies":"10.200.0.5"}"""),
            Stored(SettingsSectionNames.Machines, """{"zeroTouchNetworks":"10.200.0.0/16"}"""),
        ]);

        Assert.StartsWith("The zero touch network 10.200.0.0/16 contains the proxy 10.200.0.5.", Assert.Single(snapshot[SettingsSectionNames.Machines].Problems).Message, StringComparison.Ordinal);
        Assert.False(snapshot[SettingsSectionNames.Proxies].Closed);
        Assert.False(snapshot.Machines.ZeroTouchEnabled);
        Assert.Single(snapshot.ForwardedHeaders.KnownProxies);
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void EveryAddressIsNoNetwork(string network)
    {
        SettingsSnapshot snapshot = Build([Stored(SettingsSectionNames.Machines, $$"""{"zeroTouchNetworks":"{{network}}"}""")]);

        Assert.Contains("is every address there is", Assert.Single(snapshot[SettingsSectionNames.Machines].Problems).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWideNetworkIsAWarningToConfirm()
    {
        SettingsSnapshot snapshot = Build([Stored(SettingsSectionNames.Machines, """{"zeroTouchNetworks":"10.0.0.0/8, 10.200.0.0/16, fd00::/32"}""")]);

        SettingsSectionState machines = snapshot[SettingsSectionNames.Machines];

        Assert.Empty(machines.Problems);
        Assert.Equal(2, machines.Warnings.Count);
        Assert.All(machines.Warnings, warning => Assert.Equal(SettingWarningCodes.WideNetwork, warning.Code));
        Assert.Contains(machines.Warnings, warning => warning.Message.StartsWith("10.0.0.0/8 is wider than a /16.", StringComparison.Ordinal));
        Assert.Contains(machines.Warnings, warning => warning.Message.StartsWith("fd00::/32 is wider than a /48.", StringComparison.Ordinal));
    }

    // A field is named on the page as the stored document names it, and an entry of a map in brackets. A key may hold
    // colons, as a claim value does, and only a member of the map's entries ends it.
    [Theory]
    [InlineData(SettingsSectionNames.Deployment, "Domain:UserName", "domain.userName")]
    [InlineData(SettingsSectionNames.Pxe, "BootTargets:X64Uefi:Method", "bootTargets[X64Uefi].method")]
    [InlineData(SettingsSectionNames.Pxe, "BootTargets:X64Uefi:ServerHostName", "bootTargets[X64Uefi].serverHostName")]
    [InlineData(SettingsSectionNames.Pxe, "BootTargets:X64:Uefi", "bootTargets[X64:Uefi]")]
    [InlineData(SettingsSectionNames.Ldap, "GroupRoleMap:CN=Admins,DC=corp", "groupRoleMap[CN=Admins,DC=corp]")]
    [InlineData(SettingsSectionNames.Oidc, "GroupRoleMap:urn:example:admins", "groupRoleMap[urn:example:admins]")]
    [InlineData(SettingsSectionNames.Oidc, "GroupRoleMap:urn:example:method", "groupRoleMap[urn:example:method]")]
    [InlineData(SettingsSectionNames.Logging, "LogLevel:Microsoft.AspNetCore", "logLevel[Microsoft.AspNetCore]")]
    [InlineData(SettingsSectionNames.Pxe, "HttpBootPort", "httpBootPort")]
    [InlineData(SettingsSectionNames.Ldap, "", "")]
    public void AProblemIsNamedAsThePageNamesItsField(string section, string path, string expected)
    {
        Assert.Equal(expected, SettingsDefinitions.Find(section)!.PageName(path));
    }

    // A value of another build that no longer converts takes its default, and the section names the field.
    [Fact]
    public void AStoredValueThatNoLongerConvertsTakesItsDefault()
    {
        SettingsSnapshot snapshot = Build([Stored(SettingsSectionNames.Machines, """{"maxWaiting":"many","requireWebApproval":true,"unknownMember":1}""")]);

        SettingsSectionState machines = snapshot[SettingsSectionNames.Machines];

        Assert.Equal("MaxWaiting", Assert.Single(machines.Problems).Field);
        Assert.Equal(10_000, ((DDT.Server.Machines.MachineOptions)machines.Options).MaxWaiting);
        Assert.True(((DDT.Server.Machines.MachineOptions)machines.Options).RequireWebApproval);
    }

    [Fact]
    public void ThePxeSectionTakesTheBootstrapValuesFromConfiguration()
    {
        SettingsSnapshot snapshot = Build(
            [Stored(SettingsSectionNames.Pxe, """{"httpBootPort":1,"interfaces":"eth0"}""")],
            ("DDT:Pxe:HttpBootPort", "8081"),
            ("DDT:StorePath", Path.Combine(Path.GetTempPath(), "ddt-store")));

        Assert.Equal(8081, snapshot.Pxe!.HttpBootPort);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "ddt-store", "boot"), snapshot.Pxe.BootDirectory);
        Assert.Equal("eth0", snapshot.Pxe.Interfaces);
    }

    internal static StoredSettingsSection Stored(
        string section,
        string values,
        long version = 3,
        Dictionary<string, StoredSecret>? secrets = null) =>
        new(section, SettingsStore.SchemaVersion, JsonNode.Parse(values)!.AsObject(), secrets ?? [], version, DateTimeOffset.UnixEpoch, null, "tester");

    internal static SettingsSnapshot Build(IEnumerable<StoredSettingsSection> stored, params (string Key, string Value)[] settings) =>
        SettingsSnapshot.Build(
            stored.ToDictionary(section => section.Section),
            new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
                .Build());
}

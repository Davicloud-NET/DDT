// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Unattend;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Xunit;

namespace DDT.Server.Tests;

public sealed class UnattendRendererSetupTests
{
    private const string ConfiguredKey = "NPPR9-FWDCX-D2C8J-H872K-2YT43";
    private const string RuleKey = "W269N-WFGWX-YVC9B-4J6C9-T83GX";

    private static readonly WriteUnattendStep s_step = new() { Id = Guid.NewGuid(), Name = "Answer file" };

    private static readonly DeploymentOptions s_options = new() { Organization = "Davicloud", Owner = "IT", ProductKey = ConfiguredKey };

    private static UnattendSources Sources(DeploymentOptions? options = null)
    {
        DeploymentOptions deployment = options ?? s_options;
        RunInputs inputs = RunInputs.Capture(new Machine { SmbiosUuid = "uuid", PrimaryMac = "00155D010203", AssignedName = "PC-0001" }, deployment, DateTimeOffset.UtcNow);

        return new UnattendSources(inputs) { ImageLanguage = "en-US", ProductKey = deployment.ProductKey, MacAddress = "00155D010203" };
    }

    private static (UnattendSettings? Settings, string? Problem) Settings(WriteUnattendStep step, params (string Name, string Value)[] values)
    {
        Dictionary<string, string> byName = values.ToDictionary(value => value.Name, value => value.Value, StringComparer.OrdinalIgnoreCase);

        return new UnattendRenderer().Settings(Sources(), step, name => byName.GetValueOrDefault(name));
    }

    // UnattendWriterPinTests holds that file byte for byte.
    [Fact]
    public void WithoutTheNewSettingsTheAnswerFileIsWhatItWas()
    {
        (UnattendSettings? settings, string? problem) = new UnattendRenderer().Settings(Sources(new DeploymentOptions()), s_step, _ => null);

        Assert.Null(problem);
        Assert.Equal(
            new UnattendSettings("amd64", "PC-0001", null, "en-US", "en-US", "en-US", null),
            settings);
    }

    [Fact]
    public void TakesTheConfiguredKeyOrganizationAndOwner()
    {
        (UnattendSettings? settings, string? problem) = Settings(s_step);

        Assert.Null(problem);
        Assert.Equal(ConfiguredKey, settings!.ProductKey?.Value);
        Assert.Equal(("Davicloud", "IT"), (settings.RegisteredOrganization, settings.RegisteredOwner));
        Assert.Equal(OobePages.AllHidden, settings.Oobe);
        Assert.Null(settings.Network);
    }

    [Fact]
    public void ARunValueOverridesTheConfiguredKey()
    {
        (UnattendSettings? settings, _) = Settings(s_step, ("ProductKey", RuleKey.ToLowerInvariant()));

        Assert.Equal(RuleKey, settings!.ProductKey?.Value);
    }

    [Theory]
    [InlineData("12345", "The run's ProductKey value is not five groups of five letters or digits.")]
    [InlineData(null, "The product key on the Deployment defaults page is not five groups of five letters or digits.")]
    public void NamesWhereAWrongKeyCameFromButNeverTheKey(string? ruleKey, string expected)
    {
        UnattendSources sources = Sources() with { ProductKey = "WRONG-KEY" };
        Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase) { ["ProductKey"] = ruleKey };

        (string? answerFile, string? problem) = new UnattendRenderer().Render(sources, s_step, name => values.GetValueOrDefault(name));

        Assert.Null(answerFile);
        Assert.Equal(expected, problem);
    }

    [Fact]
    public void TheStepOverridesTheDefaultsAndPicksTheLanguageAndPages()
    {
        WriteUnattendStep step = s_step with
        {
            UiLanguage = "{{Language}}",
            Organization = "Branch {{Site}}",
            Owner = "Lab",
            HideOnlineAccounts = false,
        };

        (UnattendSettings? settings, string? problem) = Settings(step, ("Language", "de-AT"), ("Site", "Graz"));

        Assert.Null(problem);
        Assert.Equal(("de-AT", "Branch Graz", "Lab"), (settings!.UiLanguage, settings.RegisteredOrganization, settings.RegisteredOwner));
        Assert.Equal(new OobePages(true, true, false, true), settings.Oobe);
    }

    [Fact]
    public void RefusesADisplayLanguageTheValuesMakeThatIsNoTag()
    {
        (_, string? problem) = Settings(s_step with { UiLanguage = "{{Language}}" }, ("Language", "German"));

        Assert.Equal("The answer file's display language, German, is not a language tag such as de-DE.", problem);
    }

    [Fact]
    public void SetsAStaticNetworkOnTheMachinesPrimaryAdapter()
    {
        (UnattendSettings? settings, string? problem) = Settings(
            s_step,
            ("IPAddress", "10.0.4.51/24"),
            ("Gateway", "10.0.4.1"),
            ("DnsServers", "10.0.4.2; 10.0.4.3"));

        Assert.Null(problem);
        StaticNetwork network = settings!.Network!;
        Assert.Equal("00-15-5D-01-02-03", network.Identifier);
        Assert.Equal(["10.0.4.51/24"], network.Addresses);
        Assert.Equal("10.0.4.1", network.Gateway);
        Assert.Equal(["10.0.4.2", "10.0.4.3"], network.DnsServers);
    }

    [Fact]
    public void WithoutAnAddressTheMachineKeepsDhcp()
    {
        (UnattendSettings? settings, _) = Settings(s_step, ("Gateway", "10.0.4.1"), ("DnsServers", "10.0.4.2"));

        Assert.Null(settings!.Network);
    }

    [Theory]
    [InlineData("IPAddress", "10.0.4.51", "The run's IPAddress value, 10.0.4.51, is not a list")]
    [InlineData("Gateway", "router", "The run's Gateway value, router, is not an IPv4 address.")]
    [InlineData("DnsServers", "10.0.4.2;dns", "The run's DnsServers value, 10.0.4.2;dns, is not a list")]
    public void NamesTheNetworkValueSetupCouldNotTake(string name, string value, string expected)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase) { ["IPAddress"] = "10.0.4.51/24", [name] = value };

        (_, string? problem) = new UnattendRenderer().Settings(Sources(), s_step, key => values.GetValueOrDefault(key));

        Assert.StartsWith(expected, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsTheSecretsOutOfTheSourcesText()
    {
        string text = (Sources() with { Password = "Local password 7" }).ToString();

        Assert.DoesNotContain(ConfiguredKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain("password", text, StringComparison.Ordinal);
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DDT.Contracts;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using Xunit;

namespace DDT.Server.Tests;

// A step stored or posted without the version 7 fields gets their defaults, not false or 0.
public sealed class StepDefaultsTests
{
    private const string Identity =
        "processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\" "
        + "xmlns:wcm=\"http://schemas.microsoft.com/WMIConfig/2002/State\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"";

    private const string Version6 = """
        {"version":6,"steps":[
          {"id":"0193a4b2-0000-7000-8000-000009600003","name":"Write the answer file","localAdministrator":false,"kind":"writeUnattend"},
          {"kind":"windowsUpdate","id":"0193a4b2-0000-7000-8000-000009600004","name":"Windows Update"}
        ]}
        """;

    public static TheoryData<string> Contexts => ["server", "agent"];

    private static JsonTypeInfo<SequenceDefinition> Context(string name) =>
        name == "agent" ? AgentJsonContext.Default.SequenceDefinition : DdtJsonContext.Default.SequenceDefinition;

    [Theory]
    [MemberData(nameof(Contexts))]
    public void AnAnswerFileStepWithoutTheSwitchesHidesThePagesAndLogsOnOnce(string context)
    {
        WriteUnattendStep step = Assert.IsType<WriteUnattendStep>(JsonSerializer.Deserialize(Version6, Context(context))?.Steps[0]);

        Assert.Equal((true, true, true, true, 1), (step.HideEulaPage, step.HideOemRegistration, step.HideOnlineAccounts, step.HideWirelessSetup, step.AutoLogonCount));
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void AWindowsUpdateStepWithoutSettingsTakesMicrosoftProductsAndEightRestarts(string context)
    {
        WindowsUpdateStep step = Assert.IsType<WindowsUpdateStep>(JsonSerializer.Deserialize(Version6, Context(context))?.Steps[1]);

        Assert.Equal((true, 8), (step.MicrosoftProducts, step.AllowedRestarts));
    }

    [Fact]
    public void WrittenFieldsStillWin()
    {
        string json = """{"version":7,"steps":[{"kind":"windowsUpdate","id":"0193a4b2-0000-7000-8000-000009600004","name":"U","microsoftProducts":false,"restartLimit":3}]}""";

        WindowsUpdateStep step = Assert.IsType<WindowsUpdateStep>(JsonSerializer.Deserialize(json, DdtJsonContext.Default.SequenceDefinition)?.Steps[0]);

        Assert.Equal((false, 3), (step.MicrosoftProducts, step.AllowedRestarts));
    }

    [Fact]
    public void AVersion6SequenceRendersTheAnswerFileAsBefore()
    {
        string expected = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <unattend xmlns="urn:schemas-microsoft-com:unattend">
              <settings pass="specialize">
                <component name="Microsoft-Windows-Shell-Setup" {Identity}>
                  <ComputerName>*</ComputerName>
                </component>
              </settings>
              <settings pass="oobeSystem">
                <component name="Microsoft-Windows-International-Core" {Identity}>
                  <InputLocale>de-DE</InputLocale>
                  <SystemLocale>de-DE</SystemLocale>
                  <UILanguage>de-DE</UILanguage>
                  <UserLocale>de-DE</UserLocale>
                </component>
                <component name="Microsoft-Windows-Shell-Setup" {Identity}>
                  <OOBE>
                    <HideEULAPage>true</HideEULAPage>
                    <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>
                    <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
                    <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
                    <ProtectYourPC>3</ProtectYourPC>
                  </OOBE>
                </component>
              </settings>
            </unattend>
            """;
        WriteUnattendStep step = Assert.IsType<WriteUnattendStep>(SequenceDocuments.Read(Version6).Steps[0]);
        RunInputs inputs = RunInputs.Capture(new Machine { SmbiosUuid = "uuid", PrimaryMac = "020000000001" }, new DeploymentOptions(), DateTimeOffset.UtcNow);

        (string? written, string? problem) = new UnattendRenderer().Render(new UnattendSources(inputs) { ImageLanguage = "de-DE" }, step, _ => null);

        Assert.Null(problem);
        Assert.Equal(expected.ReplaceLineEndings("\r\n"), written);
    }
}

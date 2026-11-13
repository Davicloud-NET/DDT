// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Core.Unattend;
using Xunit;

namespace DDT.Core.Tests;

// The answer file as sequences before version 7 get it, byte for byte. Nothing new may change it.
public sealed class UnattendWriterPinTests
{
    private const string Identity =
        "processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\" "
        + "xmlns:wcm=\"http://schemas.microsoft.com/WMIConfig/2002/State\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"";

    private static string Encoded(string password) => Convert.ToBase64String(Encoding.Unicode.GetBytes(password + "Password"));

    [Fact]
    public void WritesTheAnswerFileWithoutAnAdministratorAsBefore()
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

        string written = UnattendWriter.Write(new UnattendSettings("amd64", "*", null, "de-DE", "de-DE", "de-DE", null));

        Assert.Equal(expected.ReplaceLineEndings("\r\n"), written);
    }

    [Fact]
    public void WritesTheAnswerFileWithAnAdministratorAsBefore()
    {
        string expected = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <unattend xmlns="urn:schemas-microsoft-com:unattend">
              <settings pass="specialize">
                <component name="Microsoft-Windows-Shell-Setup" {Identity}>
                  <ComputerName>PC-0042</ComputerName>
                  <TimeZone>W. Europe Standard Time</TimeZone>
                </component>
                <component name="Microsoft-Windows-Deployment" {Identity}>
                  <RunSynchronous>
                    <RunSynchronousCommand wcm:action="add">
                      <Order>1</Order>
                      <Path>net accounts /maxpwage:unlimited</Path>
                    </RunSynchronousCommand>
                  </RunSynchronous>
                </component>
              </settings>
              <settings pass="oobeSystem">
                <component name="Microsoft-Windows-International-Core" {Identity}>
                  <InputLocale>0407:00000407</InputLocale>
                  <SystemLocale>de-DE</SystemLocale>
                  <UILanguage>en-US</UILanguage>
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
                  <UserAccounts>
                    <LocalAccounts>
                      <LocalAccount wcm:action="add">
                        <Name>Admin</Name>
                        <Group>Administrators</Group>
                        <Password>
                          <Value>{Encoded("Adm1n-Secret")}</Value>
                          <PlainText>false</PlainText>
                        </Password>
                      </LocalAccount>
                    </LocalAccounts>
                  </UserAccounts>
                </component>
              </settings>
            </unattend>
            """;

        string written = UnattendWriter.Write(new UnattendSettings(
            "amd64",
            "PC-0042",
            "W. Europe Standard Time",
            "en-US",
            "de-DE",
            "0407:00000407",
            new LocalAdministrator("Admin", "Adm1n-Secret")));

        Assert.Equal(expected.ReplaceLineEndings("\r\n"), written);
    }
}

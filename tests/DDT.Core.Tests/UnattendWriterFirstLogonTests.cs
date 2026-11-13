// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Core.Unattend;
using Xunit;

namespace DDT.Core.Tests;

// The automatic logon and the first-logon commands, checked as the text Setup reads.
public sealed class UnattendWriterFirstLogonTests
{
    private const string Password = "S3cret <&> \"pass\"";

    private static readonly UnattendSettings s_plain = new("amd64", "PC-0042", "UTC", "de-DE", "de-DE", "de-DE", null);

    private static string Lines(string text) => text.ReplaceLineEndings("\r\n");

    private static string Encoded(string password) => Convert.ToBase64String(Encoding.Unicode.GetBytes(password + "Password"));

    [Fact]
    public void WritesTheAutomaticLogonFirstWithTheEncodedPasswordAndOneLessLogon()
    {
        string xml = UnattendWriter.Write(s_plain with { AutoLogon = new AutoLogonAccount("kiosk", "CORP", Password, 3) });

        Assert.Contains(
            Lines($"""
                      <AutoLogon>
                        <Password>
                          <Value>{Encoded(Password)}</Value>
                          <PlainText>false</PlainText>
                        </Password>
                        <Domain>CORP</Domain>
                        <Enabled>true</Enabled>
                        <LogonCount>2</LogonCount>
                        <Username>kiosk</Username>
                      </AutoLogon>
                      <OOBE>
                """),
            xml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(Password, xml, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstLogonCommands", xml, StringComparison.Ordinal);
    }

    // Setup adds 1 to the count, so a single logon needs a count of 1 and a first command that sets it to 0.
    [Fact]
    public void EndsASingleLogonWithTheFirstCommand()
    {
        string xml = UnattendWriter.Write(s_plain with
        {
            AutoLogon = new AutoLogonAccount("Admin", null, Password, 1),
            FirstLogonCommands = ["cmd /c echo one"],
        });

        Assert.Contains("<LogonCount>1</LogonCount>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Domain>", xml, StringComparison.Ordinal);
        Assert.Contains(
            Lines("""
                      <FirstLogonCommands>
                        <SynchronousCommand wcm:action="add">
                          <CommandLine>reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoLogonCount /t REG_DWORD /d 0 /f</CommandLine>
                          <Order>1</Order>
                          <RequiresUserInput>false</RequiresUserInput>
                        </SynchronousCommand>
                        <SynchronousCommand wcm:action="add">
                          <CommandLine>cmd /c echo one</CommandLine>
                          <Order>2</Order>
                          <RequiresUserInput>false</RequiresUserInput>
                        </SynchronousCommand>
                      </FirstLogonCommands>
                      <OOBE>
                """),
            xml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WritesTheCommandsInOrderWithoutAnAutomaticLogon()
    {
        string xml = UnattendWriter.Write(s_plain with { FirstLogonCommands = ["cmd /c echo \"a & b\"", @"msiexec /i C:\x.msi /qn"] });

        Assert.Contains(
            Lines("""
                      <FirstLogonCommands>
                        <SynchronousCommand wcm:action="add">
                          <CommandLine>cmd /c echo "a &amp; b"</CommandLine>
                          <Order>1</Order>
                          <RequiresUserInput>false</RequiresUserInput>
                        </SynchronousCommand>
                        <SynchronousCommand wcm:action="add">
                          <CommandLine>msiexec /i C:\x.msi /qn</CommandLine>
                          <Order>2</Order>
                          <RequiresUserInput>false</RequiresUserInput>
                        </SynchronousCommand>
                      </FirstLogonCommands>
                """),
            xml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AutoLogon", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsThePasswordOutOfTheSettingsText()
    {
        Assert.DoesNotContain(Password, (s_plain with { AutoLogon = new AutoLogonAccount("kiosk", null, Password, 1) }).ToString(), StringComparison.Ordinal);
    }
}

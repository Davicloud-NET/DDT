// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Core.Unattend;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class UnattendFileTests : IDisposable
{
    private readonly string _windows = Directory.CreateTempSubdirectory("ddt-unattend-").FullName;

    public void Dispose() => Directory.Delete(_windows, recursive: true);

    [Fact]
    public async Task WritesTheAnswerFileWhereSetupLooksAndCreatesTheCleanupScript()
    {
        await UnattendFile.WriteAsync(_windows, TestImage.Unattend, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_windows, "Windows", "Panther", "unattend.xml"), UnattendFile.PathIn(_windows));
        Assert.Equal(TestImage.Unattend, await File.ReadAllTextAsync(UnattendFile.PathIn(_windows), TestContext.Current.CancellationToken));
        Assert.Equal(
            "del /q /f \"%WINDIR%\\Panther\\unattend.xml\"\r\n",
            await File.ReadAllTextAsync(Path.Combine(_windows, "Windows", "Setup", "Scripts", "SetupComplete.cmd"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PutsTheCleanupBeforeTheImagesOwnSetupCompleteAndKeepsItsBytes()
    {
        string scripts = Path.Combine(_windows, "Windows", "Setup", "Scripts");
        Directory.CreateDirectory(scripts);

        // Code page 850 text and an exit that would skip every line after it.
        byte[] own = [.. "echo "u8, 0x84, .. "\r\nexit /b 0\r\n"u8];
        await File.WriteAllBytesAsync(Path.Combine(scripts, "SetupComplete.cmd"), own, TestContext.Current.CancellationToken);

        await UnattendFile.WriteAsync(_windows, TestImage.Unattend, TestContext.Current.CancellationToken);

        byte[] expected = [.. Encoding.ASCII.GetBytes($"{UnattendFile.CleanupLine}\r\n"), .. own];
        Assert.Equal(expected, await File.ReadAllBytesAsync(Path.Combine(scripts, "SetupComplete.cmd"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SummarizesWithoutSecrets()
    {
        string summary = UnattendFile.Summarize(TestImage.Unattend);

        Assert.Equal(
            "computer name PC-042, time zone the default for the locale, UI language en-US, locale de-DE, keyboard de-DE, " +
            "local administrator yes, domain join no",
            summary);
        Assert.DoesNotContain("c2VjcmV0", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesAStaticAddressAndThatAKeyIsSetButNeverTheKey()
    {
        string unattend = UnattendWriter.Write(new UnattendSettings("amd64", "PC-042", null, "en-US", "de-DE", "de-DE", null)
        {
            ProductKey = new ProductKey("NPPR9-FWDCX-D2C8J-H872K-2YT43"),
            Network = new StaticNetwork("00-15-5D-01-02-03", ["10.0.4.51/24"], "10.0.4.1", []),
        });

        string summary = UnattendFile.Summarize(unattend);

        Assert.EndsWith("domain join no, product key set, static address 10.0.4.51/24", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("NPPR9", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesTheAutomaticLogonAndCountsTheCommandsButNeverSaysThePassword()
    {
        string unattend = UnattendWriter.Write(new UnattendSettings("amd64", "PC-042", null, "en-US", "de-DE", "de-DE", null)
        {
            AutoLogon = new AutoLogonAccount("Admin", null, "Kiosk-Secret-1", 2),
            FirstLogonCommands = ["cmd /c echo one", "cmd /c echo two"],
        });

        string summary = UnattendFile.Summarize(unattend);

        Assert.EndsWith("domain join no, automatic logon as Admin, 2 first-logon commands", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Kiosk-Secret-1", unattend, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnAnswerFileThatIsNotXml() =>
        Assert.Throws<DeploymentStepException>(() => UnattendFile.Summarize("<unattend>"));
}

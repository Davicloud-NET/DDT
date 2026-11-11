// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Contracts.Applications;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WindowsApplicationDetectorTests
{
    private const string ProductCode = "{23170F69-40C1-2702-2301-000001000000}";
    private const string ToolPath = @"C:\Program Files\Tool\tool.exe";

    private readonly FakeDetectionSources _machine = new();

    private WindowsApplicationDetector Detector => new(_machine, _machine, _machine);

    [Fact]
    public void FindsAProductWindowsInstallerKnows()
    {
        _machine.Products.Add(ProductCode);

        Assert.True(Detector.IsInstalled(new ProductCodeDetection(ProductCode)));
        Assert.False(Detector.IsInstalled(new ProductCodeDetection("{00000000-0000-0000-0000-000000000001}")));
    }

    [Fact]
    public void FindsAnUninstallKey()
    {
        _machine.UninstallKeys.Add("7-Zip");

        Assert.True(Detector.IsInstalled(new UninstallKeyDetection(" 7-Zip ")));
        Assert.False(Detector.IsInstalled(new UninstallKeyDetection("Notepad++")));
    }

    [Fact]
    public void FindsAFileAfterExpandingItsVariables()
    {
        _machine.Files[ToolPath] = null;

        Assert.True(Detector.IsInstalled(new FileDetection(@"%ProgramFiles%\Tool\tool.exe")));
        Assert.False(Detector.IsInstalled(new FileDetection(@"%ProgramFiles%\Other\other.exe")));
    }

    [Theory]
    [InlineData("23.1.0.0", "23.1", true)]
    [InlineData("23.1.0.0", "23.01", true)]
    [InlineData("24.0.0.0", "23.9", true)]
    [InlineData("23.0.9.0", "23.1", false)]
    [InlineData(null, "1.0", false)]
    public void AFileWithAMinimumVersionMustHaveAtLeastThatVersion(string? version, string minimum, bool installed)
    {
        _machine.Files[ToolPath] = version;

        Assert.Equal(installed, Detector.IsInstalled(new FileDetection(@"%ProgramFiles%\Tool\tool.exe", minimum)));
    }

    [Theory]
    [InlineData("1.10", "1.9", true)]
    [InlineData("1.9", "1.10", false)]
    [InlineData("2", "2.0.0.0", true)]
    [InlineData("2.0.0.1", "2", true)]
    [InlineData("10.0.26100.1", "10.0.26100.2", false)]
    [InlineData("65535.0", "65535", true)]
    public void ComparesVersionsPartByPartAsNumbers(string version, string minimum, bool atLeast)
    {
        Assert.Equal(atLeast, FileVersions.IsAtLeast(version, minimum));
    }

    // Against this computer's Windows Installer and files.
    [Fact]
    public void WindowsInstallerKnowsNoMadeUpProductAndRefusesNoGuid()
    {
        MsiInstalledProducts products = new();

        Assert.False(products.IsInstalled("{0193A4B2-0000-7000-8000-0000000DD7A9}"));
        Assert.False(products.IsInstalled("not a product code"));
    }

    [Fact]
    public void ReadsTheVersionOfAFileAsSystemWouldFindIt()
    {
        SystemDetectionFiles files = new();
        string cmd = files.Expand(@"%SystemRoot%\System32\cmd.exe");

        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmd, ignoreCase: true);
        Assert.True(files.Exists(cmd));
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", files.VersionOf(cmd));
    }

    [Fact]
    public void AMadeUpUninstallKeyIsNotThere()
    {
        Assert.False(new RegistryUninstallKeys().Exists("DDT test key that is not there 0193a4b2"));
    }
}

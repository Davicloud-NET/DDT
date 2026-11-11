// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Contracts.Applications;
using Microsoft.Win32;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WindowsApplicationDetectorTests
{
    private const string ProductCode = "{23170F69-40C1-2702-2301-000001000000}";
    private const string ToolPath = @"C:\Program Files\Tool\tool.exe";

    private readonly FakeDetectionSources _machine = new();

    private WindowsApplicationDetector Detector => new(_machine, _machine, _machine, _machine);

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

    [Fact]
    public void FindsAProvisionedPackageOfTheFamily()
    {
        _machine.Provisioned.AddRange(["Microsoft.GetHelp_10.2409.42652.0_neutral_~_8wekyb3d8bbwe", "Contoso.App_1.2.3.0_x64__8wekyb3d8bbwe"]);

        Assert.True(Detector.IsInstalled(new PackageFamilyDetection(" contoso.app_8WEKYB3D8BBWE ")));
        Assert.False(Detector.IsInstalled(new PackageFamilyDetection("Contoso.App_cw5n1h2txyewy")));
        Assert.False(Detector.IsInstalled(new PackageFamilyDetection("Contoso_8wekyb3d8bbwe")));
    }

    // App Installer 1.21 in the image does not count for an upload of 1.29.
    [Theory]
    [InlineData("Microsoft.DesktopAppInstaller_1.21.10120.0_x64__8wekyb3d8bbwe", "1.29.30.0", false)]
    [InlineData("Microsoft.DesktopAppInstaller_1.29.30.0_x64__8wekyb3d8bbwe", "1.29.30.0", true)]
    [InlineData("Microsoft.DesktopAppInstaller_2026.917.151.0_neutral_~_8wekyb3d8bbwe", "1.29.30.0", true)]
    [InlineData("Microsoft.DesktopAppInstaller_1.21.10120.0_x64__8wekyb3d8bbwe", null, true)]
    [InlineData("Microsoft.DesktopAppInstaller_1.29_x64__8wekyb3d8bbwe", "1.29.30.0", false)]
    public void APackageFamilyWithAVersionNeedsAtLeastThatOne(string fullName, string? minimum, bool installed)
    {
        _machine.Provisioned.Add(fullName);

        Assert.Equal(installed, Detector.IsInstalled(new PackageFamilyDetection("Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", minimum)));
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

    // Against this computer's all-user store. A Windows Server may have nothing provisioned.
    [Fact]
    public void FindsAFamilyThisComputerProvisionedButNoMadeUpOne()
    {
        RegistryProvisionedPackages store = new();
        WindowsApplicationDetector detector = new(_machine, _machine, _machine, store);

        foreach (string[] parts in store.FullNames().Select(name => name.Split('_')).Where(parts => parts.Length == 5).Take(3))
        {
            Assert.True(detector.IsInstalled(new PackageFamilyDetection($"{parts[0]}_{parts[4]}")));
        }

        Assert.False(detector.IsInstalled(new PackageFamilyDetection("Davicloud.MadeUp0193a4b2_8wekyb3d8bbwe")));
    }

    [Fact]
    public void AMadeUpUninstallKeyIsNotThere()
    {
        Assert.False(new RegistryUninstallKeys().Exists("DDT test key that is not there 0193a4b2"));
    }

    // The test's own HKCU stands in for SYSTEM's, where a per-user installer the agent runs registers.
    [Fact]
    public void AKeyOfTheInstallingAccountCounts()
    {
        string path = $@"Software\DDT-test-{Guid.NewGuid():N}\Uninstall";
        Registry.CurrentUser.CreateSubKey($@"{path}\{{9B3415F5-0000-7000-8000-000000000001}}").Dispose();

        try
        {
            RegistryUninstallKeys keys = new(path);

            Assert.True(keys.Exists("{9B3415F5-0000-7000-8000-000000000001}"));
            Assert.False(keys.Exists("{9B3415F5-0000-7000-8000-000000000002}"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path[..path.LastIndexOf('\\')], throwOnMissingSubKey: false);
        }
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Applications.Msix;
using Xunit;

namespace DDT.Core.Tests.Applications.Msix;

public sealed class PackageFamilyNamesTests
{
    // Publisher and family name as Get-AppxPackage showed them for packages installed on a Windows 11 PC.
    [Theory]
    [InlineData(TestMsix.Microsoft, "8wekyb3d8bbwe")]
    [InlineData("CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", "cw5n1h2txyewy")]
    [InlineData("CN=4975D53F-AA7E-49A5-8B49-EA4FDC1BB66B", "qbz5n2kfra8p0")]
    [InlineData("CN=EB51A5DA-0E72-4863-82E4-EA21C1F8DFE3", "8j3eq9eme6ctt")]
    [InlineData("CN=\"Notepad++\", O=\"Notepad++\", L=Saint Cloud, S=Ile-de-France, C=FR", "7njy0v32s6xk6")]
    public void DerivesThePublisherIdAsWindowsDoes(string publisher, string id) =>
        Assert.Equal(id, PackageFamilyNames.PublisherId(publisher));

    [Fact]
    public void JoinsTheNameAndThePublisherId() =>
        Assert.Equal("PythonSoftwareFoundation.Python.3.12_qbz5n2kfra8p0", PackageFamilyNames.Of("PythonSoftwareFoundation.Python.3.12", "CN=4975D53F-AA7E-49A5-8B49-EA4FDC1BB66B"));

    [Theory]
    [InlineData(PackageFamilyNames.Example)]
    [InlineData("Microsoft.DesktopAppInstaller_8WEKYB3D8BBWE")]
    [InlineData("1527c705-839a-4832-9118-54d4Bd6a0c89_cw5n1h2txyewy")]
    public void AcceptsAFamilyName(string value) => Assert.True(PackageFamilyNames.IsFamilyName(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Microsoft.DesktopAppInstaller")]
    [InlineData("Microsoft.DesktopAppInstaller_8wekyb3d8bbw")]
    [InlineData("Microsoft.DesktopAppInstaller_8wekyb3d8bbwi")]
    [InlineData("Microsoft.DesktopAppInstaller_2026.917.151.0_neutral_~_8wekyb3d8bbwe")]
    [InlineData("Ab_8wekyb3d8bbwe")]
    [InlineData("Contoso App_8wekyb3d8bbwe")]
    public void RefusesWhatIsNoFamilyName(string? value) => Assert.False(PackageFamilyNames.IsFamilyName(value));

    [Theory]
    [InlineData("Microsoft.DesktopAppInstaller_2026.917.151.0_neutral_~_8wekyb3d8bbwe", true)]
    [InlineData("microsoft.desktopappinstaller_1.0.0.0_x64__8WEKYB3D8BBWE", true)]
    [InlineData("Microsoft.DesktopAppInstallerPreview_1.0.0.0_x64__8wekyb3d8bbwe", false)]
    [InlineData("Microsoft.DesktopAppInstaller_1.0.0.0_x64__cw5n1h2txyewy", false)]
    [InlineData("Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", false)]
    public void TellsWhetherAPackageFullNameIsOfTheFamily(string fullName, bool isOf) =>
        Assert.Equal(isOf, PackageFamilyNames.IsOfFamily(fullName, PackageFamilyNames.Example));
}

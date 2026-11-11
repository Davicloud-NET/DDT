// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Messages;
using DDT.Core.Applications;
using Xunit;

namespace DDT.Core.Tests.Applications;

public sealed class ApplicationDetectionRulesTests
{
    [Theory]
    [InlineData("{23170F69-40C1-2702-2501-000001000000}")]
    [InlineData("23170f69-40c1-2702-2501-000001000000")]
    [InlineData(" {23170f69-40c1-2702-2501-000001000000} ")]
    public void ReadsAProductCodeWithOrWithoutBraces(string code) =>
        Assert.Equal("{23170F69-40C1-2702-2501-000001000000}", ApplicationDetectionRules.NormalizedProductCode(code));

    [Theory]
    [InlineData("")]
    [InlineData("7-Zip")]
    [InlineData("(23170F69-40C1-2702-2501-000001000000)")]
    [InlineData("23170F6940C12702250100000100000")]
    public void RefusesWhatIsNoProductCode(string code) =>
        ApplicationRulesTests.AssertProblem(
            ApplicationRulesTests.Command with { Detection = new ProductCodeDetection(code) },
            "detection.productCode",
            ServerMessages.ApplicationProductCode);

    [Theory]
    [InlineData("7-Zip")]
    [InlineData("{23170F69-40C1-2702-2501-000001000000}")]
    [InlineData("Mozilla Firefox 132.0 (x64 en-US)")]
    public void AcceptsAnUninstallKeyName(string key) => Assert.True(ApplicationDetectionRules.IsUninstallKey(key));

    [Theory]
    [InlineData("")]
    [InlineData(@"Uninstall\7-Zip")]
    [InlineData("SOFTWARE/7-Zip")]
    [InlineData("Line\nbreak")]
    public void RefusesAKeyNameWithAPath(string key) =>
        ApplicationRulesTests.AssertProblem(
            ApplicationRulesTests.Command with { Detection = new UninstallKeyDetection(key) },
            "detection.keyName",
            ServerMessages.ApplicationUninstallKey);

    [Fact]
    public void RefusesAKeyNameOverTheRegistrysLimit() =>
        Assert.False(ApplicationDetectionRules.IsUninstallKey(new string('k', ApplicationLimits.MaxUninstallKeyLength + 1)));

    [Theory]
    [InlineData(@"C:\Program Files\7-Zip\7z.exe")]
    [InlineData(@"%ProgramFiles%\7-Zip\7z.exe")]
    [InlineData(@"%ProgramFiles(x86)%\App\app.exe")]
    [InlineData(@"%SystemRoot%\System32\app.dll")]
    [InlineData(@"d:\app.exe")]
    public void AcceptsAFullPath(string path) => Assert.True(ApplicationDetectionRules.IsFilePath(path));

    [Theory]
    [InlineData("")]
    [InlineData("app.exe")]
    [InlineData(@"\Program Files\app.exe")]
    [InlineData(@"\\server\share\app.exe")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Program Files\")]
    [InlineData(@"C:\Program Files\\app.exe")]
    [InlineData(@"C:\Program Files\..\app.exe")]
    [InlineData(@"C:\app.exe:stream")]
    [InlineData(@"C:\a?.exe")]
    [InlineData(@"%ProgramFiles%app.exe")]
    [InlineData(@"%ProgramFiles%\%App%\app.exe")]
    [InlineData(@"%%\app.exe")]
    [InlineData("C:/Program Files/app.exe")]
    public void RefusesWhatIsNoFullPathToAFile(string path) => Assert.False(ApplicationDetectionRules.IsFilePath(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("24")]
    [InlineData("24.09")]
    [InlineData("1.2.3.4")]
    [InlineData("65535.0.0.65535")]
    public void AcceptsAVersion(string? version) => Assert.True(ApplicationDetectionRules.IsVersion(version));

    [Theory]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.65536")]
    [InlineData("v1.2")]
    [InlineData("1.2-beta")]
    [InlineData("1..2")]
    [InlineData("１.２")]
    public void RefusesWhatIsNoFileVersion(string version) => Assert.False(ApplicationDetectionRules.IsVersion(version));

    [Fact]
    public void ChecksTheFileAndItsVersion() =>
        Assert.Equal(
            ["detection.path", "detection.minimumVersion"],
            ApplicationRules.Check(ApplicationRulesTests.Command with { Detection = new FileDetection("7z.exe", "x") }, null).Select(problem => problem.Field));

    [Fact]
    public void NoDetectionIsFine() => Assert.Empty(ApplicationRules.Check(ApplicationRulesTests.Command with { Detection = null }, null));
}

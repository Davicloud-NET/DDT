// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Applications.Installers;
using Xunit;

namespace DDT.Server.Tests.Applications;

public sealed class InstallerSuggestionsTests
{
    [Theory]
    [InlineData(InstallerKind.Msi, "msiexec /i \"setup file.exe\" /qn /norestart")]
    [InlineData(InstallerKind.Burn, "\"setup file.exe\" /quiet /norestart")]
    [InlineData(InstallerKind.InnoSetup, "\"setup file.exe\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010")]
    [InlineData(InstallerKind.Nsis, "\"setup file.exe\" /S")]
    [InlineData(InstallerKind.InstallShield, "\"setup file.exe\" /s /v\"/qn /norestart\"")]
    public void EachKindGetsItsSilentCommandLine(InstallerKind kind, string commandLine)
    {
        Assert.Equal(commandLine, InstallerSuggestions.For(new InstallerFacts(kind), "setup file.exe")?.CommandLine);
    }

    [Fact]
    public void AnMsiSuggestsItsExitCodesAndItsProductCodeForDetection()
    {
        InstallerFacts facts = new(InstallerKind.Msi, ProductCode: "{6f1c2e5a-3b7d-4c9e-8a21-0d4f5b6c7e81}");

        InstallerSuggestion? suggestion = InstallerSuggestions.For(facts, "app.msi");

        Assert.NotNull(suggestion);
        Assert.Equal([0], suggestion.SuccessExitCodes);
        Assert.Equal([3010, 1641], suggestion.RestartExitCodes);
        Assert.Equal("{6F1C2E5A-3B7D-4C9E-8A21-0D4F5B6C7E81}", suggestion.ProductCode);
        Assert.Null(suggestion.UninstallKey);
    }

    [Fact]
    public void ABurnBundleSuggestsItsUninstallKeyForDetection()
    {
        InstallerFacts facts = new(InstallerKind.Burn, BundleCode: "{8F3C1A2B-4D5E-4F60-9A7B-0C1D2E3F4A5B}");

        InstallerSuggestion? suggestion = InstallerSuggestions.For(facts, "bundle.exe");

        Assert.Equal(("{8F3C1A2B-4D5E-4F60-9A7B-0C1D2E3F4A5B}", (string?)null), (suggestion?.UninstallKey, suggestion?.ProductCode));
        Assert.Equal([3010], suggestion?.RestartExitCodes);
    }

    [Fact]
    public void ABurnBundleTakesAnotherVersionAlreadyInstalledAsSuccess() =>
        Assert.Equal([0, 1638], InstallerSuggestions.For(new InstallerFacts(InstallerKind.Burn), "bundle.exe")?.SuccessExitCodes);

    [Fact]
    public void AnNsisInstallerHasNoRestartCode()
    {
        Assert.Empty(InstallerSuggestions.For(new InstallerFacts(InstallerKind.Nsis), "setup.exe")?.RestartExitCodes ?? [-1]);
    }

    [Fact]
    public void AProductCodeThatIsNoGuidIsNotSuggested()
    {
        InstallerFacts facts = new(InstallerKind.Msi, ProductCode: "not a guid");

        Assert.Null(InstallerSuggestions.For(facts, "app.msi")?.ProductCode);
    }

    [Fact]
    public void AnUnknownInstallerGetsNoSuggestion()
    {
        Assert.Null(InstallerSuggestions.For(InstallerFacts.Unknown, "tool.exe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("evil\" & calc.exe")]
    public void AFileNameACommandLineCantQuoteGetsNoSuggestion(string fileName)
    {
        Assert.Null(InstallerSuggestions.For(new InstallerFacts(InstallerKind.Nsis), fileName));
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WingetOutputTests
{
    [Theory]
    [InlineData("Found 7-Zip [7zip.7zip] Version 26.06")]
    [InlineData("Downloading https://github.com/ip7z/7zip/releases/download/26.06/7z2606-x64.exe")]
    [InlineData("Successfully installed")]
    public void WhatWingetSaysGoesToTheLog(string line) => Assert.Equal(line, WingetOutput.Line(line));

    [Theory]
    [InlineData("-")]
    [InlineData("  \\ ")]
    [InlineData("|")]
    [InlineData("/")]
    [InlineData("  ████▒▒▒▒  1.50 MB / 3.00 MB")]
    [InlineData("1.50 MB / 3.00 MB")]
    [InlineData("1024 KB / 2048 KB")]
    [InlineData("  45%")]
    [InlineData("   ")]
    public void ProgressIsLeftOut(string line) => Assert.Null(WingetOutput.Line(line));

    [Fact]
    public void TerminalEscapesAreTakenOut() =>
        Assert.Equal("Successfully verified installer hash", WingetOutput.Line("\u001B[2K\u001B[32mSuccessfully verified installer hash\u001B[0m"));
}

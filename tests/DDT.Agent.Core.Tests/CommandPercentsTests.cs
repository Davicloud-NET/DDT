// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class CommandPercentsTests
{
    [Theory]
    [InlineData("setup.exe /S", "setup.exe /S")]
    [InlineData(@"%ProgramFiles%\a b\setup.exe /S", @"%ProgramFiles%\a b\setup.exe /S")]
    [InlineData(@"""%ProgramFiles(x86)%\Tool\setup.exe"" /S", @"""%ProgramFiles(x86)%\Tool\setup.exe"" /S")]
    [InlineData("echo %DATE:~0,3% %PATH:a=b% %_x.y-1%", "echo %DATE:~0,3% %PATH:a=b% %_x.y-1%")]
    [InlineData("for %%i in (*.msi) do msiexec /i %%i /qn", "for %%i in (*.msi) do msiexec /i %%i /qn")]
    [InlineData(@"cmd.exe /c for %i in ( *.cmd ) do call ""%~fi""", @"cmd.exe /c for %%i in ( *.cmd ) do call ""%%~fi""")]
    [InlineData("curl https://example.com/a%20b.msi", "curl https://example.com/a%%20b.msi")]
    [InlineData(@"""C:\100% sure\setup.exe"" /S", @"""C:\100%% sure\setup.exe"" /S")]
    [InlineData("echo 100%", "echo 100%%")]
    [InlineData(@"for %i in (%ProgramFiles%\x) do echo %i", @"for %%i in (%ProgramFiles%\x) do echo %%i")]
    [InlineData("echo %1 %* %:x%", "echo %%1 %%* %%:x%%")]
    [InlineData("echo %unclosed:~0", "echo %%unclosed:~0")]
    public void DoublesEveryPercentThatStartsNoReference(string command, string inBatchFile)
    {
        Assert.Equal(inBatchFile, CommandPercents.ForBatchFile(command));
    }
}

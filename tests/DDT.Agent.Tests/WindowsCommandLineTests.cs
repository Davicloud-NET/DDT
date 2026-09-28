// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WindowsCommandLineTests
{
    [Fact]
    public void QuotesTheFileNameAndPlainArgumentsAsProcessDoes()
    {
        string line = WindowsCommandLine.Build(@"C:\Windows\System32\cmd.exe", ["/d", "/c", "run.cmd"]);

        Assert.Equal(@"""C:\Windows\System32\cmd.exe"" /d /c run.cmd", line);
    }

    [Theory]
    [InlineData("a b", "\"a b\"")]
    [InlineData("", "\"\"")]
    [InlineData("plain", "plain")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\path with space\", "\"C:\\path with space\\\\\"")]
    public void QuotesAnArgumentTheWayCommandLineToArgvExpects(string argument, string quoted)
    {
        string line = WindowsCommandLine.Build("tool.exe", [argument]);

        Assert.Equal($"\"tool.exe\" {quoted}", line);
    }

    [Theory]
    [InlineData(@"CORP\installer", "installer", "CORP", "installer")]
    [InlineData("installer@corp.example", "installer@corp.example", null, "installer")]
    [InlineData("localadmin", "localadmin", ".", "localadmin")]
    public void SplitsAUserNameIntoUserDomainAndProfileName(string userName, string user, string? domain, string profile)
    {
        (string gotUser, string? gotDomain, string gotProfile) = WindowsAccountSession.Split(userName);

        Assert.Equal(user, gotUser);
        Assert.Equal(domain, gotDomain);
        Assert.Equal(profile, gotProfile);
    }
}

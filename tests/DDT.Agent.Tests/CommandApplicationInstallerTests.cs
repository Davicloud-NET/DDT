// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Applications;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using Xunit;
using static DDT.Agent.Tests.TestApplications;

namespace DDT.Agent.Tests;

public sealed class CommandApplicationInstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ddt-command-").FullName;
    private readonly RecordingToolRunner _tools = new();
    private readonly AgentLog _log = new(TimeProvider.System, TextWriter.Null);

    private string Scripts => Path.Combine(_root, "scripts");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task RunsTheCommandLineAsWrittenThroughCmdAsSystemWithTheApplicationsTimeout()
    {
        Directory.CreateDirectory(Scripts);
        string file = Path.Combine(Scripts, $"{Tool.Id:D}.cmd");
        byte[]? written = null;
        _tools.AnswerExitCode = (_, _, _) =>
        {
            written = File.ReadAllBytes(file);

            return 3010;
        };
        Dictionary<string, string> environment = new() { ["DDT_APPLICATION"] = "Tool" };
        ApplicationLaunch launch = new(Tool with { Command = "\"setup 2.exe\" /S /D=%ProgramFiles%\\Tool" }, _root, Scripts, _root, environment);

        int exitCode = await new CommandApplicationInstaller(_tools, _log).InstallAsync(launch, TestContext.Current.CancellationToken);

        Assert.Equal(3010, exitCode);
        Assert.Equal([RecordingToolRunner.CommandLine(WindowsScriptLauncher.CmdPath, "/d", "/c", file)], _tools.Calls);
        Assert.Equal(Encoding.UTF8.GetBytes("@chcp 65001 >nul\r\n\"setup 2.exe\" /S /D=%ProgramFiles%\\Tool\r\n"), written);
        ToolRunOptions options = Assert.Single(_tools.Options);
        Assert.Equal((_root, TimeSpan.FromMinutes(5), (IAccountSession?)null), (options.WorkingDirectory, options.Timeout, options.Account));
        Assert.Same(environment, options.Environment);
        Assert.False(File.Exists(file));
    }

    // The real cmd runs a for loop's variables as typed, here calling both .cmd files in the working directory.
    [Fact]
    public async Task ALoopVariableReachesCmdAsTyped()
    {
        string work = Work();
        File.WriteAllText(Path.Combine(work, "first.cmd"), "@echo first> first.txt\r\n");
        File.WriteAllText(Path.Combine(work, "second.cmd"), "@echo second> second.txt\r\n");

        Assert.Equal(0, await RunInCmdAsync(@"cmd.exe /c for %i in ( *.cmd ) do call ""%~fi""", work));
        Assert.True(File.Exists(Path.Combine(work, "first.txt")));
        Assert.True(File.Exists(Path.Combine(work, "second.txt")));
    }

    [Fact]
    public async Task AReferenceStillExpandsInCmd()
    {
        string work = Work();

        Assert.Equal(0, await RunInCmdAsync("cmd.exe /c echo %SystemRoot%> out.txt", work));
        Assert.Equal(Environment.GetEnvironmentVariable("SystemRoot"), File.ReadAllText(Path.Combine(work, "out.txt")).Trim());
    }

    [Fact]
    public async Task AnApplicationWithoutACommandLineRunsNothing()
    {
        ApplicationLaunch launch = new(Tool with { Command = " " }, _root, Scripts, _root, new Dictionary<string, string>());

        DeploymentStepException refused = await Assert.ThrowsAsync<DeploymentStepException>(
            () => new CommandApplicationInstaller(_tools, _log).InstallAsync(launch, TestContext.Current.CancellationToken));

        Assert.Equal("It has no command line. Give it one in the library, then assign the sequence again.", refused.Message);
        Assert.Empty(_tools.Calls);
    }

    private string Work()
    {
        Directory.CreateDirectory(Scripts);

        return Directory.CreateDirectory(Path.Combine(_root, "work")).FullName;
    }

    private Task<int> RunInCmdAsync(string command, string work)
    {
        ApplicationLaunch launch = new(Tool with { Command = command }, work, Scripts, _root, new Dictionary<string, string>());

        return new CommandApplicationInstaller(new ToolRunner(_log, TimeProvider.System, AnsiCodePage.Decode), _log)
            .InstallAsync(launch, TestContext.Current.CancellationToken);
    }
}

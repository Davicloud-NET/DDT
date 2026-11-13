// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using DDT.Agent.WindowsPhase;
using Xunit;

namespace DDT.Agent.Tests;

// The script as cmd runs it from the RunOnce command line, in a folder with a space in its path.
public sealed class FirstLogonScriptTests : IDisposable
{
    private readonly string _parent = Directory.CreateTempSubdirectory("ddt first logon ").FullName;

    private string Folder => Path.Combine(_parent, "FirstLogon");

    private string Script => Path.Combine(Folder, FirstLogonScript.FileName);

    private string Output => Path.Combine(_parent, "out.txt");

    public void Dispose() => Directory.Delete(_parent, recursive: true);

    private async Task RunAsync(params string[] commands)
    {
        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(Script, FirstLogonScript.File(commands), TestContext.Current.CancellationToken);
        string line = FirstLogonScript.RunOnceCommand(Script);
        string cmd = $"\"{Path.Combine(Environment.SystemDirectory, "cmd.exe")}\"";
        Assert.StartsWith(cmd + " ", line, StringComparison.Ordinal);

        ProcessStartInfo start = new(Path.Combine(Environment.SystemDirectory, "cmd.exe"), line[(cmd.Length + 1)..])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _parent,
        };
        start.Environment["DDT_TEST_OUT"] = Output;
        start.Environment["DDT_TEST_WORD"] = "word";

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("cmd.exe did not start.");
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunsTheCommandsInOrderPastAFailureAndExpandsOnlyReferences()
    {
        await RunAsync(
            "echo one 100% %DDT_TEST_WORD%>>\"%DDT_TEST_OUT%\"",
            "exit /b 5",
            "no-such-command-ddt",
            "echo two>>\"%DDT_TEST_OUT%\"");

        Assert.Equal(["one 100% word", "two"], await File.ReadAllLinesAsync(Output, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeletesItselfAndItsEmptyFolder()
    {
        await RunAsync("echo one>>\"%DDT_TEST_OUT%\"");

        Assert.False(Directory.Exists(Folder));
        Assert.True(File.Exists(Output));
    }

    [Fact]
    public async Task KeepsAFolderThatHoldsMore()
    {
        Directory.CreateDirectory(Folder);
        await File.WriteAllTextAsync(Path.Combine(Folder, "other.txt"), "other", TestContext.Current.CancellationToken);

        await RunAsync("echo one>>\"%DDT_TEST_OUT%\"");

        Assert.False(File.Exists(Script));
        Assert.True(File.Exists(Path.Combine(Folder, "other.txt")));
    }

    [Fact]
    public void TheRunOnceCommandLineStaysFarBelow260Characters()
    {
        string line = FirstLogonScript.RunOnceCommand(Path.Combine(FirstLogonScript.Folder, FirstLogonScript.FileName));

        Assert.True(line.Length < 130, line);
    }
}

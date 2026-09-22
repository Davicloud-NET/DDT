// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;
using Xunit;

namespace DDT.Agent.Tests;

// The agent's removal from the installed Windows, with the run's directory as the Windows phase leaves it.
public sealed class AgentRemovalTests : IDisposable
{
    private readonly FakeDeploymentTools _tools = new();
    private readonly RecordingToolRunner _toolRunner = new();
    private readonly StringWriter _console = new();

    public AgentRemovalTests()
    {
        // What the service control manager's deletion found left of the run.
        _toolRunner.Answer = (fileName, arguments) =>
        {
            _tools.Note($"run {Path.GetFileName(fileName)} {string.Join(' ', arguments)} with {string.Join(", ", Left())}");

            return [];
        };
    }

    public void Dispose()
    {
        _tools.Dispose();
        _console.Dispose();
    }

    private string Windows => _tools.Volumes.Windows;

    private string DdtDirectory => Path.Combine(Windows, "DDT");

    [Fact]
    public async Task DeletesTheRunThenTheServiceAndLeavesWhatIsInUseToTheNextStartOfWindows()
    {
        Write(@"run\token", "run-token-1");
        Write(@"run\state.json", "{}");
        Write(@"agent\ddt-agent.exe", "MZ");
        Write(@"agent\agent.json", "{}");
        Write(@"logs\agent.log", "12:00:00 INFO  The run is done.");
        Write(@"logs\dism-1.log", "DISM");
        Write(@"scripts\1.cmd", "echo 1");
        Write(@"packages\1\setup.exe", "MZ");
        Write(@"cache\5a5a", "zip");
        Directory.CreateDirectory(Path.Combine(DdtDirectory, "scratch"));

        await RemoveAsync();

        Assert.Equal(
            [
                @"run sc.exe delete DdtSequence with agent\agent.json, agent\ddt-agent.exe, logs\agent.log",
                @"delete at restart DDT\agent\agent.json",
                @"delete at restart DDT\agent\ddt-agent.exe",
                @"delete at restart DDT\agent",
                @"delete at restart DDT\logs\agent.log",
                @"delete at restart DDT\logs",
                @"delete at restart DDT",
            ],
            _tools.Calls);
        Assert.Equal(RecordingToolRunner.CommandLine(AgentRemoval.ScPath, "delete", OfflineServiceRegistration.ServiceName), Assert.Single(_toolRunner.Calls));
        Assert.StartsWith(Environment.SystemDirectory, AgentRemoval.ScPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WARN", _console.ToString(), StringComparison.Ordinal);

        _tools.DeleteMarkedAsWindowsStarts();

        Assert.False(Directory.Exists(DdtDirectory));
        Assert.True(Directory.Exists(Windows));
    }

    // Also what the service finds when its run was never handed over completely, or its state is already gone.
    [Fact]
    public async Task MarksOnlyWhatIsThere()
    {
        Write(@"agent\ddt-agent.exe", "MZ");

        await RemoveAsync();

        Assert.Equal(
            [
                @"run sc.exe delete DdtSequence with agent\ddt-agent.exe",
                @"delete at restart DDT\agent\ddt-agent.exe",
                @"delete at restart DDT\agent",
                @"delete at restart DDT",
            ],
            _tools.Calls);
    }

    // Whatever stops the removal half way, nothing left can act as the machine: the token goes by its own name before
    // anything else. Here nothing can even list the run's directory.
    [Fact]
    public async Task TheRunTokenGoesFirst()
    {
        Write(@"run\token", "run-token-1");
        Write(@"run\state.json", "{}");
        Write(@"agent\ddt-agent.exe", "MZ");
        _toolRunner.Answer = (_, _) => [];

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        FileSystemAccessRule unlisted = new(identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        DirectoryInfo run = new(Path.Combine(DdtDirectory, "run"));
        DirectorySecurity security = run.GetAccessControl();
        security.AddAccessRule(unlisted);
        run.SetAccessControl(security);

        try
        {
            await RemoveAsync();
        }
        finally
        {
            security.RemoveAccessRule(unlisted);
            run.SetAccessControl(security);
        }

        Assert.False(File.Exists(Path.Combine(run.FullName, "token")));
        Assert.False(File.Exists(Path.Combine(run.FullName, "state.json")));
    }

    // A script may leave a process behind, such as an installer's helper, which Windows ends when it restarts: the
    // program it runs from a package goes then, and so does the folder it works in.
    [Fact]
    public async Task WhatAProcessStillHoldsGoesWhenWindowsNextStarts()
    {
        Write(@"run\token", "run-token-1");
        Write(@"agent\ddt-agent.exe", "MZ");
        Write(@"packages\1\tray.exe", "MZ");
        string scripts = Path.Combine(DdtDirectory, "scripts");
        Directory.CreateDirectory(scripts);

        using (FileStream program = File.Open(Path.Combine(DdtDirectory, "packages", "1", "tray.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
        using (Process helper = await StartInAsync(scripts))
        {
            try
            {
                await RemoveAsync();
            }
            finally
            {
                helper.Kill();
                await helper.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }

        Assert.Equal(
            [
                @"run sc.exe delete DdtSequence with agent\ddt-agent.exe, packages\1\tray.exe",
                @"delete at restart DDT\agent\ddt-agent.exe",
                @"delete at restart DDT\agent",
                @"delete at restart DDT\packages\1\tray.exe",
                @"delete at restart DDT\packages\1",
                @"delete at restart DDT\packages",
                @"delete at restart DDT\scripts",
                @"delete at restart DDT",
            ],
            _tools.Calls);

        _tools.DeleteMarkedAsWindowsStarts();

        Assert.False(Directory.Exists(DdtDirectory));
    }

    // Whatever a link in the directory leads to is not DDT's to delete.
    [Fact]
    public async Task ALinkIsMarkedItselfAndNeverFollowed()
    {
        Write(@"agent\ddt-agent.exe", "MZ");
        string elsewhere = Path.Combine(_tools.Root, "Elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "kept.txt"), "kept");
        string link = Path.Combine(DdtDirectory, "agent", "link");

        using (Process mklink = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", "mklink", "/J", link, elsewhere])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        }) ?? throw new InvalidOperationException("cmd.exe did not start."))
        {
            await mklink.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, mklink.ExitCode);
        }

        try
        {
            await RemoveAsync();
        }
        finally
        {
            // Unlike a recursive delete, which fails on a junction without an administrator, this deletes only the link.
            Directory.Delete(link);
        }

        Assert.Equal(
            [
                @"run sc.exe delete DdtSequence with agent\ddt-agent.exe, agent\link\kept.txt",
                @"delete at restart DDT\agent\ddt-agent.exe",
                @"delete at restart DDT\agent\link",
                @"delete at restart DDT\agent",
                @"delete at restart DDT",
            ],
            _tools.Calls);
        Assert.True(File.Exists(Path.Combine(elsewhere, "kept.txt")));
    }

    [Fact]
    public async Task AServiceThatCannotBeDeletedIsAWarningAndTheRestStillGoes()
    {
        Write(@"run\token", "run-token-1");
        Write(@"agent\ddt-agent.exe", "MZ");
        _toolRunner.Answer = (_, _) => throw new DeploymentStepException("sc.exe failed with exit code 0x00000424. Its output is in the machine log.");

        await RemoveAsync();

        Assert.Equal([@"delete at restart DDT\agent\ddt-agent.exe", @"delete at restart DDT\agent", @"delete at restart DDT"], _tools.Calls);
        Assert.Contains(
            "WARN  The service DdtSequence could not be deleted (sc.exe failed with exit code 0x00000424. Its output is in the machine log.). Delete it with sc.exe delete DdtSequence.",
            _console.ToString(),
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(DdtDirectory, "run", "token")));
    }

    private Task RemoveAsync() =>
        new AgentRemoval(Windows, _toolRunner, _tools, new AgentLog(new ImmediateTimeProvider(), _console))
            .RemoveAsync(TestContext.Current.CancellationToken);

    // A process that works in directory until it is ended, and has started by the time this returns.
    private static async Task<Process> StartInAsync(string directory)
    {
        Process process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c pause")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        }) ?? throw new InvalidOperationException("cmd.exe did not start.");

        // pause says so once it waits.
        char[] said = new char[1];
        await process.StandardOutput.ReadAsync(said, TestContext.Current.CancellationToken);

        return process;
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(DdtDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // The files under the run's directory, relative to it.
    private IEnumerable<string> Left() =>
        Directory.Exists(DdtDirectory)
            ? Directory.EnumerateFiles(DdtDirectory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(DdtDirectory, path))
                .Order(StringComparer.OrdinalIgnoreCase)
            : [];
}

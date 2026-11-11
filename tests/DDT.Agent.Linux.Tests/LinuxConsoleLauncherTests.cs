// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using DDT.Agent.Consoles;
using Xunit;

namespace DDT.Agent.Tests;

// Real processes stand in for the console: a shell script that takes the pipe's arguments and waits.
public sealed class LinuxConsoleLauncherTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("ddt-console-launcher-").FullName;

    [Fact]
    public async Task StopsTheConsoleWithSigtermSoItGivesTheTerminalBackItself()
    {
        using IConsoleProcess console = Start("trap 'exit 7' TERM; while :; do sleep 0.1; done");
        await Task.Delay(300, TestContext.Current.CancellationToken);

        console.Stop();

        Assert.Equal(7, await console.Exited.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KillsAConsoleThatIsStillThereASecondLater()
    {
        using IConsoleProcess console = Start("trap '' TERM; while :; do sleep 0.1; done");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Stopwatch clock = Stopwatch.StartNew();

        console.Stop();
        int code = await console.Exited.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(137, code);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(5));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private IConsoleProcess Start(string body)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The console's launcher signals Linux processes.");

        string script = Path.Combine(_folder, "ddt-console");
        File.WriteAllText(script, $"#!/bin/sh\n{body}\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return new LinuxConsoleLauncher(script).Start("ddt-console-test");
    }
}

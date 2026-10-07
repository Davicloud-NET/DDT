// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ShellScriptLauncherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ddt-shell-test-{Guid.NewGuid():N}");

    public ShellScriptLauncherTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static RunScriptStep Script(string script, ScriptInterpreter interpreter = ScriptInterpreter.Shell) => new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000c001"),
        Name = "Script",
        Phase = SequencePhase.LinuxBoot,
        Interpreter = interpreter,
        Script = script,
    };

    private async Task<(ScriptLaunch Launch, string File)> WrittenAsync(string script)
    {
        RunScriptStep step = Script(script);
        ScriptLaunch launch = await new ShellScriptLauncher(dryRun: true).WriteAsync(step, _directory, TestContext.Current.CancellationToken);

        return (launch, Path.Combine(_directory, $"{step.Id:D}.sh"));
    }

    // A byte order mark or a carriage return would end up in the first command, or in the interpreter's name.
    [Fact]
    public async Task WritesTheScriptWithLineFeedsAndWithoutAByteOrderMark()
    {
        (_, string file) = await WrittenAsync("echo \"$DDT_PHASE\"\r\necho Größe");

        Assert.Equal("echo \"$DDT_PHASE\"\necho Größe\n"u8.ToArray(), await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(file));
        }
    }

    [Fact]
    public async Task StartsAScriptWithoutAFirstLineForAnInterpreterWithTheShell()
    {
        (ScriptLaunch launch, string file) = await WrittenAsync("echo hello\n");

        Assert.Equal("/bin/sh", launch.FileName);
        Assert.Equal([file], launch.Arguments);
    }

    // The interpreter gets the file, so nothing depends on the file being executable where it lies.
    [Theory]
    [InlineData("#!/bin/bash\necho hello", "/bin/bash", null)]
    [InlineData("#!/bin/bash -eu\r\necho hello", "/bin/bash", "-eu")]
    [InlineData("#! /usr/bin/env  bash\necho hello", "/usr/bin/env", "bash")]
    [InlineData("#!/usr/bin/env -S python3 -u\nprint(1)", "/usr/bin/env", "-S python3 -u")]
    [InlineData("#!/usr/bin/python3", "/usr/bin/python3", null)]
    public async Task StartsTheInterpreterTheFirstLineNamesWithItsOneArgument(string script, string interpreter, string? argument)
    {
        (ScriptLaunch launch, string file) = await WrittenAsync(script);

        Assert.Equal(interpreter, launch.FileName);
        Assert.Equal(argument is null ? [file] : [argument, file], launch.Arguments);
    }

    [Theory]
    [InlineData("#!\necho hello")]
    [InlineData("#!   \necho hello")]
    [InlineData("echo hello\n#!/bin/bash")]
    [InlineData(" #!/bin/bash\necho hello")]
    [InlineData("")]
    public void TakesOnlyAFirstLineThatNamesAnInterpreter(string script) => Assert.Null(ShellScriptLauncher.Interpreter(script));

    [Fact]
    public void SaysWhatAScriptNeedsThatTheSystemLacks()
    {
        ShellScriptLauncher launcher = new();
        RunScriptStep named = Script("#!/opt/ddt-test/no-such-interpreter -x\necho hello");

        Assert.Equal(
            "This system has no /opt/ddt-test/no-such-interpreter, which the script's first line names. Name an interpreter it has, " +
            "or leave the line out for /bin/sh.",
            launcher.Missing(named, SequencePhase.LinuxBoot));
        Assert.Equal(ShellScriptLauncher.NotAShellScriptMessage, launcher.Missing(Script("echo hello", ScriptInterpreter.Cmd), SequencePhase.LinuxBoot));
        Assert.Equal(ShellScriptLauncher.NotAShellScriptMessage, launcher.Missing(Script("exit 0", ScriptInterpreter.PowerShell), SequencePhase.LinuxBoot));
    }

    // A dry run starts nothing, on a computer that may have no shell at all.
    [Fact]
    public void ADryRunAsksForNoInterpreter()
    {
        ShellScriptLauncher launcher = new(dryRun: true);

        Assert.Null(launcher.Missing(Script("#!/opt/ddt-test/no-such-interpreter\necho hello"), SequencePhase.LinuxBoot));
        Assert.Null(launcher.Missing(Script("echo hello"), SequencePhase.LinuxBoot));
        Assert.Equal(ShellScriptLauncher.NotAShellScriptMessage, launcher.Missing(Script("echo hello", ScriptInterpreter.Cmd), SequencePhase.LinuxBoot));
    }
}

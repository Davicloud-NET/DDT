// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class RunScriptStepRunnerTests
{
    private static readonly Guid s_packageId = Guid.Parse("0193a4b2-0000-7000-8000-00000000b001");

    private static readonly RunScriptStep s_cmd = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a5"),
        Name = "Greet",
        Phase = SequencePhase.WindowsPE,
        Interpreter = ScriptInterpreter.Cmd,
        Script = "echo Grüße\nexit /b 0",
        TimeoutMinutes = 5,
    };

    private static readonly RunScriptStep s_powerShell = s_cmd with
    {
        Name = "Greet in PowerShell",
        Interpreter = ScriptInterpreter.PowerShell,
        Script = "Write-Output 'Grüße'",
    };

    // Office and Floor may be set by steps, Region only when the run starts.
    private static readonly VariableDeclaration[] s_variables =
    [
        new() { Name = "Office", SetBySteps = true },
        new() { Name = "Floor", SetBySteps = true },
        new() { Name = "Region" },
    ];

    [Fact]
    public async Task RunsACmdScriptFromTheAgentsDirectoryBeforeTheDiskIsPartitioned()
    {
        using StepRunnerFixture run = new([s_cmd]);
        string scripts = Path.Combine(run.WorkDirectory, "scripts");
        string file = Path.Combine(scripts, $"{s_cmd.Id:D}.cmd");

        StepResult result = await run.RunScript.RunAsync(s_cmd, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.Equal([RecordingToolRunner.CommandLine(RunScriptStepRunner.CmdPath, "/d", "/c", file)], run.ToolRunner.Calls);
        ToolRunOptions options = Assert.Single(run.ToolRunner.Options);
        Assert.Equal(scripts, options.WorkingDirectory);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Timeout);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["DDT_PHASE"] = "WindowsPE",
                ["DDT_RUN_ID"] = StepRunnerFixture.RunId.ToString("D"),
                ["DDT_STEP_ID"] = s_cmd.Id.ToString("D"),
            },
            options.Environment);
    }

    [Fact]
    public async Task WritesACmdScriptInUtf8AfterALineThatSwitchesCmdToUtf8()
    {
        using StepRunnerFixture run = new([s_cmd]);

        await run.RunScript.RunAsync(s_cmd, run.Context(), TestContext.Current.CancellationToken);

        byte[] file = await File.ReadAllBytesAsync(Path.Combine(run.WorkDirectory, "scripts", $"{s_cmd.Id:D}.cmd"), TestContext.Current.CancellationToken);
        Assert.Equal(Encoding.UTF8.GetBytes("@chcp 65001 >nul\r\necho Grüße\r\nexit /b 0\r\n"), file);
    }

    [Fact]
    public async Task RunsAPowerShellScriptWithAByteOrderMarkOnThePartitionedDiskFromACmdFileThatSwitchesToUtf8()
    {
        using StepRunnerFixture run = new([s_powerShell]);
        TargetVolumes volumes = run.Partitioned();
        string scripts = Path.Combine(volumes.Windows, "DDT", "scripts");
        string file = Path.Combine(scripts, $"{s_powerShell.Id:D}.ps1");
        string launcher = Path.Combine(scripts, $"{s_powerShell.Id:D}.cmd");

        await run.RunScript.RunAsync(s_powerShell, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal([RecordingToolRunner.CommandLine(RunScriptStepRunner.CmdPath, "/d", "/c", launcher)], run.ToolRunner.Calls);
        byte[] written = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
        Assert.Equal([.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("Write-Output 'Grüße'\r\n")], written);
        Assert.Equal(
            $"@chcp 65001 >nul\r\n@\"{RunScriptStepRunner.PowerShellPath}\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{file}\"\r\n",
            await File.ReadAllTextAsync(launcher, TestContext.Current.CancellationToken));

        // Offline changes to the applied Windows need its letter.
        Assert.Equal(volumes.Windows, Assert.Single(run.ToolRunner.Options).Environment!["DDT_WINDOWS"]);
    }

    [Fact]
    public async Task InWindowsTheScriptGetsNoWindowsPELetter()
    {
        RunScriptStep step = s_powerShell with { Phase = SequencePhase.Windows };
        using StepRunnerFixture run = new([step]);
        run.Partitioned();

        await run.RunScript.RunAsync(step, run.Context(SequencePhase.Windows), TestContext.Current.CancellationToken);

        IReadOnlyDictionary<string, string> environment = Assert.Single(run.ToolRunner.Options).Environment!;
        Assert.Equal("Windows", environment["DDT_PHASE"]);
        Assert.False(environment.ContainsKey("DDT_WINDOWS"));
    }

    [Theory]
    [InlineData(0, StepOutcome.Done)]
    [InlineData(3010, StepOutcome.RebootRequired)]
    [InlineData(5, StepOutcome.Failed)]
    public async Task TheExitCodeDecides(int exitCode, StepOutcome outcome)
    {
        using StepRunnerFixture run = new([s_cmd]);
        run.ToolRunner.AnswerExitCode = (_, _, _) => exitCode;

        StepResult result = await run.RunScript.RunAsync(s_cmd, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(outcome, result.Outcome);

        // Kept as LastExitCode, for a repeat that tries again until a script works.
        Assert.Equal(exitCode, result.ExitCode);

        if (outcome == StepOutcome.Failed)
        {
            Assert.Equal("The script ended with exit code 5, which is not one of its success codes (0).", result.Error);
        }
    }

    // In its environment, never in its text: what the run started with and what steps set, but not the agent's own
    // variables, and no name an environment cannot hold. A sequence without variables for steps to set gives the script
    // no file to set them in.
    [Fact]
    public async Task GivesTheScriptTheRunsValuesAndVariables()
    {
        using StepRunnerFixture run = new([s_cmd]);
        StepContext context = run.Context(
            variables: new Dictionary<string, string>
            {
                [RunVariables.WindowsPartition] = "0193a4b2-0000-7000-8000-0000000000e1",
                ["Floor"] = "3",
                [MachineVariableNames.LastExitCode] = "0",
            },
            values: new Dictionary<string, string> { ["Office"] = "Vienna", ["Floor"] = "1", ["Not a name"] = "x" });

        await run.RunScript.RunAsync(s_cmd, context, TestContext.Current.CancellationToken);

        IReadOnlyDictionary<string, string> environment = Assert.Single(run.ToolRunner.Options).Environment!;
        Assert.Equal(
            [("DDT_VAR_Floor", "3"), ("DDT_VAR_LastExitCode", "0"), ("DDT_VAR_Office", "Vienna")],
            environment.Where(pair => pair.Key.StartsWith(RunScriptStepRunner.VariablePrefix, StringComparison.Ordinal))
                .Select(pair => (pair.Key, pair.Value))
                .Order());
        Assert.False(environment.ContainsKey(RunScriptStepRunner.VariablesOut));
        Assert.DoesNotContain(await File.ReadAllTextAsync(Path.Combine(run.WorkDirectory, "scripts", $"{s_cmd.Id:D}.cmd"), TestContext.Current.CancellationToken), "Vienna", StringComparison.Ordinal);
    }

    // Only the variables the sequence lets steps set are taken, in its spelling, and only their names reach the log.
    [Fact]
    public async Task TakesTheVariablesTheSequenceLetsStepsSet()
    {
        using StepRunnerFixture run = new([s_cmd], variables: s_variables);
        string? file = null;
        run.ToolRunner.AnswerExitCode = (_, _, options) =>
        {
            file = options.Environment![RunScriptStepRunner.VariablesOut];
            File.WriteAllText(file, "office = Graz-Süd \r\n\r\nFloor=4\nRegion=EU\nUnknown=secret-looking\nno equals here\n");

            return 0;
        };

        StepResult result = await run.RunScript.RunAsync(s_cmd, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.Equal(new Dictionary<string, string> { ["Office"] = "Graz-Süd", ["Floor"] = "4" }, result.Outputs);
        Assert.Equal(Path.Combine(run.WorkDirectory, "scripts", $"{s_cmd.Id:D}.variables"), file);
        Assert.False(File.Exists(file));

        List<string> lines = [.. (await run.SentLinesAsync()).Select(line => line.Message)];
        Assert.Contains($"Line 6 of {RunScriptStepRunner.VariablesOut} is not Name=Value, so it was not read.", lines);
        Assert.Contains("The script set Region, Unknown, which the sequence does not let steps set, so the run does not take them.", lines);
        Assert.Contains("The script set Office, Floor.", lines);
        Assert.DoesNotContain(lines, line => line.Contains("Graz", StringComparison.Ordinal) || line.Contains("secret", StringComparison.Ordinal));
    }

    // A failed script sets nothing, and a restart code counts as done.
    [Theory]
    [InlineData(5, false)]
    [InlineData(3010, true)]
    public async Task TakesVariablesOnlyFromAScriptThatWorked(int exitCode, bool taken)
    {
        using StepRunnerFixture run = new([s_cmd], variables: s_variables);
        run.ToolRunner.AnswerExitCode = (_, _, options) =>
        {
            File.WriteAllText(options.Environment![RunScriptStepRunner.VariablesOut], "Floor=4\n");

            return exitCode;
        };

        StepResult result = await run.RunScript.RunAsync(s_cmd, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(taken ? new Dictionary<string, string> { ["Floor"] = "4" } : null, result.Outputs);
        Assert.Equal(exitCode, result.ExitCode);
    }

    // At most 64 lines of at most 1024 characters, however much the script writes.
    [Fact]
    public async Task ReadsNoMoreThanTheLinesAVariablesFileMayHold()
    {
        using StepRunnerFixture run = new([s_cmd], variables: s_variables);
        run.ToolRunner.AnswerExitCode = (_, _, options) =>
        {
            File.WriteAllLines(
                options.Environment![RunScriptStepRunner.VariablesOut],
                [$"Office={new string('x', RunScriptStepRunner.MaxOutputLineLength)}", .. Enumerable.Repeat("Floor=4", 63), "Floor=5"]);

            return 0;
        };

        StepResult result = await run.RunScript.RunAsync(s_cmd, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(new Dictionary<string, string> { ["Floor"] = "4" }, result.Outputs);
        List<string> lines = [.. (await run.SentLinesAsync()).Select(line => line.Message)];
        Assert.Contains($"Line 1 of {RunScriptStepRunner.VariablesOut}, for Office, is longer than 1024 characters, so it was not read.", lines);
        Assert.Contains($"The script wrote more than 64 lines to {RunScriptStepRunner.VariablesOut}, and those after line 64 were not read.", lines);
    }

    // The real interpreters: cmd writes UTF-8 after the chcp line, Windows PowerShell's Out-File UTF-16 with a byte order
    // mark.
    [Theory]
    [InlineData(ScriptInterpreter.Cmd, ">\"%DDT_VARIABLES_OUT%\" echo Office=Grüße aus Wien\r\n>>\"%DDT_VARIABLES_OUT%\" echo Floor=%DDT_VAR_Floor%")]
    [InlineData(ScriptInterpreter.PowerShell, "\"Office=Grüße aus Wien\", \"Floor=$env:DDT_VAR_Floor\" | Out-File $env:DDT_VARIABLES_OUT")]
    public async Task ARealScriptSetsVariables(ScriptInterpreter interpreter, string script)
    {
        RunScriptStep step = s_cmd with { Interpreter = interpreter, Script = script };
        using StepRunnerFixture run = new([step], variables: s_variables);
        RunScriptStepRunner runner = new(new ToolRunner(run.Log, TimeProvider.System), run.Downloads, run.Session, run.Log, run.WorkDirectory);

        StepResult result = await runner.RunAsync(
            step,
            run.Context(values: new Dictionary<string, string> { ["Floor"] = "7" }),
            TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        Assert.Equal(new Dictionary<string, string> { ["Office"] = "Grüße aus Wien", ["Floor"] = "7" }, result.Outputs);
    }

    [Fact]
    public async Task UnpacksThePackageAsTheWorkingDirectory()
    {
        RunScriptStep step = s_cmd with { PackageId = s_packageId, Script = "call setup.cmd" };
        TestZip package = new(("setup.cmd", "echo installing"));
        using StepRunnerFixture run = new([step], packages: [new AgentRunPackage(step.Id, "Office", package.Sha256, package.Content.Length)]);
        TargetVolumes volumes = run.Partitioned();
        run.Server.ServeFile(package.Sha256, package.Content);
        string unpacked = Path.Combine(volumes.Windows, "DDT", "packages", step.Id.ToString("D"));
        bool unpackedWhenRun = false;
        run.ToolRunner.AnswerExitCode = (_, _, options) =>
        {
            unpackedWhenRun = File.Exists(Path.Combine(options.WorkingDirectory!, "setup.cmd"));

            return 0;
        };

        await run.RunScript.RunAsync(step, run.Context(), TestContext.Current.CancellationToken);

        ToolRunOptions options = Assert.Single(run.ToolRunner.Options);
        Assert.Equal(unpacked, options.WorkingDirectory);
        Assert.Equal(unpacked, options.Environment!["DDT_PACKAGE"]);
        Assert.True(unpackedWhenRun);
        Assert.False(Directory.Exists(unpacked));
    }

    [Fact]
    public async Task APackageBeforeThePartitionFailsWithoutRunningTheScript()
    {
        RunScriptStep step = s_cmd with { PackageId = s_packageId };
        using StepRunnerFixture run = new([step], packages: [new AgentRunPackage(step.Id, "Office", new string('0', 64), 10)]);

        DeploymentStepException exception = await Assert.ThrowsAsync<DeploymentStepException>(
            () => run.RunScript.RunAsync(step, run.Context(), TestContext.Current.CancellationToken));

        Assert.StartsWith("A script's package is unpacked on the partitioned disk", exception.Message, StringComparison.Ordinal);
        Assert.Empty(run.ToolRunner.Calls);
    }

    // The real cmd, to show that the chcp line and the UTF-8 file work together, and a restart code wins.
    [Fact]
    public async Task RunsARealCmdScriptAndLogsWhatItPrints()
    {
        RunScriptStep step = s_cmd with { Script = "echo Grüße aus dem Skript\r\nexit /b 3010" };
        using StepRunnerFixture run = new([step]);
        RunScriptStepRunner runner = new(new ToolRunner(run.Log, TimeProvider.System), run.Downloads, run.Session, run.Log, run.WorkDirectory);

        StepResult result = await runner.RunAsync(step, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.RebootRequired, result.Outcome);
        Assert.Contains(await run.SentLinesAsync(), line => line.Message == "Grüße aus dem Skript");
    }

    // The real tree, which writes into a pipe in the ANSI code page even after chcp 65001.
    [Fact]
    public async Task LogsWhatTheRealTreePrintsWithItsUmlauts()
    {
        using StepRunnerFixture run = new([s_cmd]);
        string folder = Path.Combine(run.WorkDirectory, "tree", "für");
        Directory.CreateDirectory(folder);
        RunScriptStep step = s_cmd with { Script = $"tree \"{Path.GetDirectoryName(folder)}\"" };
        RunScriptStepRunner runner = new(new ToolRunner(run.Log, TimeProvider.System), run.Downloads, run.Session, run.Log, run.WorkDirectory);

        await runner.RunAsync(step, run.Context(), TestContext.Current.CancellationToken);

        Assert.Contains(await run.SentLinesAsync(), line => line.Message.EndsWith("für", StringComparison.Ordinal));
    }

    [Fact]
    public void ALauncherKeepsThePercentSignsOfAPath()
    {
        string launcher = Encoding.UTF8.GetString(RunScriptStepRunner.PowerShellLauncher(@"C:\Windows\powershell.exe", @"C:\100% sure\a.ps1"));

        Assert.Contains("-File \"C:\\100%% sure\\a.ps1\"", launcher, StringComparison.Ordinal);
    }

    // The real PowerShell: what it and a console program it starts print arrives intact, and its exit code decides.
    [Fact]
    public async Task RunsARealPowerShellScript()
    {
        RunScriptStep step = s_powerShell with
        {
            // The raw bytes are "für" in the ANSI code page, as tree writes it into a pipe whatever the console's code page is.
            Script = "Write-Output \"step $env:DDT_STEP_ID\"\nWrite-Output 'Grüße für Ä'\ncmd /d /c echo Größe\n" +
                "[Console]::Out.Flush(); $raw = [Console]::OpenStandardOutput(); $raw.Write([byte[]](0x66, 0xFC, 0x72, 13, 10), 0, 5); $raw.Flush()\n" +
                "[Console]::Error.WriteLine('Fehler: öß')\nexit 7",
            SuccessExitCodes = [7],
        };
        using StepRunnerFixture run = new([step]);
        RunScriptStepRunner runner = new(new ToolRunner(run.Log, TimeProvider.System), run.Downloads, run.Session, run.Log, run.WorkDirectory);

        StepResult result = await runner.RunAsync(step, run.Context(), TestContext.Current.CancellationToken);

        Assert.Equal(StepOutcome.Done, result.Outcome);
        List<AgentLogLine> lines = await run.SentLinesAsync();
        Assert.Contains(lines, line => line.Message == $"step {step.Id:D}");
        Assert.Contains(lines, line => line.Message == "Grüße für Ä");
        Assert.Contains(lines, line => line.Message == "Größe");
        Assert.Contains(lines, line => line.Message == "für");
        Assert.Contains(lines, line => line.Message == "Fehler: öß");
    }
}

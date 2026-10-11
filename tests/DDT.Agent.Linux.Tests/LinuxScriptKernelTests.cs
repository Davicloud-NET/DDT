// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// Real scripts through the real tool runner, with the kernel's own signals and file mode mask. Stopping one reaches
// every process below the test's own that started since, and the mask is the whole process's, so these tests run
// while no other test starts a process or makes a file.
[Trait("Category", "LinuxKernel")]
[Collection(ProcessesCollection.Name)]
public sealed class LinuxScriptKernelTests : IDisposable
{
    private readonly StringWriter _console = new();
    private readonly AgentLog _log;

    // Each test's processes sleep for a time no other process on the computer sleeps for.
    private readonly string _time = $"86400.{Environment.ProcessId}{Random.Shared.Next(1000, 9999)}";

    public LinuxScriptKernelTests() => _log = new AgentLog(new ImmediateTimeProvider(), _console);

    public void Dispose() => _console.Dispose();

    private string[] Lines() =>
        [.. _console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[9..])];

    // Runs the script until its time limit of three seconds stops it.
    private async Task<DeploymentStepException> StoppedAsync(string script, TimeSpan? grace = null)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "This test calls the Linux kernel.");
        Assert.SkipUnless(LinuxStrayProcesses.Adopt(), "This kernel hands a process no orphans.");

        LinuxStrayProcesses strays = new("/", _log, Environment.ProcessId) { Adopts = true, Grace = grace ?? TimeSpan.FromSeconds(5) };
        ToolRunner tools = new(_log, TimeProvider.System, Latin1Output.Decode) { Strays = strays };

        return await Assert.ThrowsAsync<DeploymentStepException>(() => tools.RunForExitCodeAsync(
            "/bin/sh",
            ["-c", script],
            new ToolRunOptions(Timeout: TimeSpan.FromSeconds(3)),
            TestContext.Current.CancellationToken));
    }

    // The ids of the living processes whose command line holds the text.
    private static List<int> Sleeping(string text)
    {
        List<int> found = [];

        foreach (string folder in Directory.GetDirectories("/proc"))
        {
            try
            {
                bool alive = File.ReadAllText(Path.Combine(folder, "stat")) is { } stat && stat[(stat.LastIndexOf(')') + 2)..][0] is not ('Z' or 'X');

                if (alive
                    && File.ReadAllText(Path.Combine(folder, "cmdline")).Contains(text, StringComparison.Ordinal)
                    && int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                {
                    found.Add(id);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return found;
    }

    // The script leaves three processes its own tree does not hold: one in a session of its own, one whose parent
    // ended at once, and one started with nohup. Being asked reaches each of them.
    [Fact]
    public async Task AScriptThatIsStoppedTakesWithItWhatItStartedOutsideItsOwnTree()
    {
        string script = $"setsid sleep {_time} & (sleep {_time} &) ; nohup sleep {_time} > /dev/null 2>&1 & echo started; sleep {_time}; echo done";

        DeploymentStepException stopped = await StoppedAsync(script);

        Assert.EndsWith("so it was stopped with every process it started.", stopped.Message, StringComparison.Ordinal);
        Assert.Contains("INFO  started", Lines());
        Assert.Empty(Sleeping(_time));
        Assert.Contains("INFO  sh and the 4 processes it had started ended when they were asked to (SIGTERM).", Lines());
        Assert.DoesNotContain(Lines(), line => line.StartsWith("WARN", StringComparison.Ordinal));
    }

    // What a script cleans up when it is asked to end: its trap runs, and what it prints still reaches the log.
    [Fact]
    public async Task AScriptThatIsStoppedGetsToCleanUpBeforeItIsEnded()
    {
        string script = $"trap 'echo cleaned up; exit 0' TERM; echo started; sleep {_time} & wait";

        await StoppedAsync(script);

        Assert.Contains("INFO  cleaned up", Lines());
        Assert.Contains("INFO  sh and the 1 process it had started ended when they were asked to (SIGTERM).", Lines());
        Assert.Empty(Sleeping(_time));
    }

    // A script that does not listen is ended outright once its moment is over, with what it started.
    [Fact]
    public async Task AScriptThatDoesNotEndWhenAskedIsEndedOutrightAfterItsGrace()
    {
        string script = $"trap '' TERM; echo started; sleep {_time}; echo done";

        await StoppedAsync(script, grace: TimeSpan.FromSeconds(1));

        Assert.Contains(
            "WARN  2 processes of sh still ran 1 s after sh was asked to end (SIGTERM), so what is left is ended outright (SIGKILL).",
            Lines());
        Assert.Empty(Sleeping(_time));
    }

    // Linux counts a program's exit code to 255, and nobody there reads it as a hexadecimal number.
    [Fact]
    public async Task SaysAToolsExitCodeAsLinuxWritesIt()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "This test calls the Linux kernel.");
        ToolRunner tools = new(_log, TimeProvider.System, Latin1Output.Decode);

        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(
            () => tools.RunAsync("/bin/sh", ["-c", "exit 3"], TestContext.Current.CancellationToken));

        Assert.Equal("sh failed with exit code 3. Its output is in the machine log.", failure.Message);
        Assert.Contains(Lines(), line => line.StartsWith("INFO  sh ended with exit code 3 after ", StringComparison.Ordinal));
    }

    // systemctl says "Removed ..." on its error output when all went well. That is no warning.
    [Fact]
    public async Task TakesTheErrorOutputOfAToolThatSaysWhatItDidThereAsPlainLines()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "This test calls the Linux kernel.");
        ToolRunner plain = new(_log, TimeProvider.System, Latin1Output.Decode) { PlainErrorOutput = new HashSet<string>(StringComparer.Ordinal) { "sh" } };
        ToolRunner other = new(_log, TimeProvider.System, Latin1Output.Decode);

        await plain.RunAsync("/bin/sh", ["-c", "echo Removed the link. >&2"], TestContext.Current.CancellationToken);
        await other.RunAsync("/bin/sh", ["-c", "echo Something is wrong. >&2"], TestContext.Current.CancellationToken);

        Assert.Contains("INFO  Removed the link.", Lines());
        Assert.Contains("WARN  Something is wrong.", Lines());
    }

    // The unit gives the agent a mask that leaves a new file to root alone. A script's files are for whom it means them.
    [Fact]
    public async Task AToolStartsWithARootShellsFileModeMaskAndTheAgentKeepsItsOwn()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "This test calls the Linux kernel.");

        const int RootAlone = 0x3F;
        ToolRunner tools = new(new AgentLog(new ImmediateTimeProvider(), TextWriter.Null), TimeProvider.System, Latin1Output.Decode)
        {
            FileModeMask = FileModeMask.Set,
        };
        int before = FileModeMask.Set(RootAlone);

        try
        {
            IReadOnlyList<string> lines = await tools.RunAsync("/bin/sh", ["-c", "umask"], TestContext.Current.CancellationToken);

            Assert.Equal(["0022"], lines);
            Assert.Equal(RootAlone, FileModeMask.Set(RootAlone));
        }
        finally
        {
            FileModeMask.Set(before);
        }
    }
}

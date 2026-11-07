// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.LinuxPhase;
using Xunit;

namespace DDT.Agent.Tests;

// What ddt-agent --linux-phase does before it has a server to talk to, over a folder that stands in for /.
public sealed class LinuxPhaseServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ddt-linux-service-{Guid.NewGuid():N}");
    private readonly StringWriter _output = new();
    private readonly LinuxAgentPaths _paths;

    public LinuxPhaseServiceTests() => _paths = new LinuxAgentPaths(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        _output.Dispose();
    }

    private void Put(string file, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    private string[] Lines() => _output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    // Someone started the agent by hand on a system that is not its own, such as a DDT server, whose store has the
    // same folder. Exit code 0, so systemd would not start it again either.
    [Fact]
    public async Task StartedWithoutItsSettingsItSaysSoInOneSentenceAndTouchesNothing()
    {
        string certificate = Path.Combine(_paths.State, "certs", "ddt-root.pem");
        Put(certificate, "the server's own");
        Put(_paths.Unit, "[Unit]");

        int exitCode = await LinuxPhaseService.RunAsync(new LinuxPhaseMachine(_paths, _output, "/home/someone/ddt-agent"), TestContext.Current.CancellationToken);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        Assert.EndsWith($"WARN  {LinuxPhaseService.NoSettings(_paths.Configuration)}", Assert.Single(Lines()), StringComparison.Ordinal);
        Assert.True(File.Exists(certificate));
        Assert.True(File.Exists(_paths.Unit));
    }

    // Settings nobody can read never get better, so the unit must not be started again every few seconds for them.
    [Fact]
    public async Task EndsWithoutAnErrorCodeWhenItsSettingsNameNoServer()
    {
        Put(_paths.Configuration, "{\"serverUrl\":\"http://not-https.example\"}");

        int exitCode = await LinuxPhaseService.RunAsync(new LinuxPhaseMachine(_paths, _output, _paths.Agent), TestContext.Current.CancellationToken);

        Assert.Equal(AgentExitCodes.Stopped, exitCode);
        string line = Assert.Single(Lines());
        Assert.Contains("ERROR An https server URL is required.", line, StringComparison.Ordinal);
        Assert.EndsWith("Without its settings the agent cannot reach the server, so it ends until Linux starts again.", line, StringComparison.Ordinal);

        // The same line is in the agent's own file, with its date.
        Assert.Equal(line, Assert.Single(File.ReadAllLines(_paths.LogFile)));
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d UTC ERROR ", line);
    }

    // journalctl -p warning then finds what went wrong. Before, every line of the agent had the priority of a notice.
    [Fact]
    public void GivesAWarningAndAnErrorTheirPriorityInTheJournalAndLeavesTheFileAsItWas()
    {
        using StringWriter file = new();
        using JournalAndFile both = new(_output, file, prioritized: true);
        AgentLog log = new(new ImmediateTimeProvider(), both);

        log.Information("Step Copy begins.");
        log.Warning("The share answered late.");
        log.Error($"The agent stopped on an unexpected error: first{Environment.NewLine}   at second");

        Assert.Equal(
            [
                "00:00:00 INFO  Step Copy begins.",
                "<4>00:00:00 WARN  The share answered late.",
                "<3>00:00:00 ERROR The agent stopped on an unexpected error: first",
                "<3>   at second",
            ],
            Lines());
        Assert.DoesNotContain('<', file.ToString());
        Assert.Equal(4, file.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    // Started by hand, the agent's lines go to a terminal, where such a prefix would only be noise.
    [Fact]
    public void WritesNoPriorityWhereTheOutputIsNotTheJournal()
    {
        using JournalAndFile both = new(_output, null);
        AgentLog log = new(new ImmediateTimeProvider(), both);

        log.Warning("The share answered late.");

        Assert.Equal(["00:00:00 WARN  The share answered late."], Lines());
    }

    [Fact]
    public void WritesEveryLineToTheJournalAlsoWhenItsFileTakesNoMore()
    {
        StringWriter file = new();
        using JournalAndFile both = new(_output, file);

        both.WriteLine("first");
        file.Dispose();
        both.WriteLine("second");
        both.Flush();

        Assert.Equal(["first", "second"], Lines());
        Assert.Equal("first" + Environment.NewLine, file.ToString());
    }
}

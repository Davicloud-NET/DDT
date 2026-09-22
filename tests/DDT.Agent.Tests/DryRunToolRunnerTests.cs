// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class DryRunToolRunnerTests : IDisposable
{
    private static readonly string s_cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private readonly string _directory = Directory.CreateTempSubdirectory("ddt-dry-tool-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task LogsATaskInsteadOfRunningIt()
    {
        (DryRunToolRunner tools, ScriptedAgentServer server, AgentLog log) = Create();
        string file = Path.Combine(_directory, "ran.txt");

        IReadOnlyList<string> output = await tools.RunAsync(s_cmd, ["/c", $"echo ran> {file}"], TestContext.Current.CancellationToken);

        Assert.Empty(output);
        Assert.False(File.Exists(file));
        await log.FlushAsync(server, Guid.Empty, "token", TestContext.Current.CancellationToken);
        Assert.Equal($"Dry run: not run: {s_cmd} /c \"echo ran> {file}\"", Assert.Single(server.SentLines).Message);
    }

    [Fact]
    public async Task TakesAScriptThatWasNotRunAsSucceeded()
    {
        (DryRunToolRunner tools, ScriptedAgentServer server, AgentLog log) = Create();
        string file = Path.Combine(_directory, "ran.txt");

        int exitCode = await tools.RunForExitCodeAsync(
            s_cmd,
            ["/d", "/c", $"echo ran> {file}& exit /b 1"],
            new ToolRunOptions(_directory, new Dictionary<string, string> { ["DDT_PHASE"] = "WindowsPE" }, TimeSpan.FromMinutes(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(file));
        await log.FlushAsync(server, Guid.Empty, "token", TestContext.Current.CancellationToken);
        Assert.Equal(
            $"Dry run: not run in {_directory}, and taken as exit code 0: {s_cmd} /d /c \"echo ran> {file}& exit /b 1\"",
            Assert.Single(server.SentLines).Message);
    }

    private static (DryRunToolRunner Tools, ScriptedAgentServer Server, AgentLog Log) Create()
    {
        AgentLog log = new(new ImmediateTimeProvider(), TextWriter.Null);

        return (new DryRunToolRunner(log), new ScriptedAgentServer(), log);
    }
}

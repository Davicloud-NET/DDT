// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.BitLocker;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

// The real tool runner with a batch file that answers as manage-bde does, printing a made-up password. Neither the
// agent's own log nor the run log the server gets ever holds it, whether the call works or fails.
public sealed class ManageBdePasswordLogTests : IDisposable
{
    private const string Password = "111111-222222-333333-444444-555555-666666-707707-121121";
    private const string Id = "{748DC9C3-3024-4646-9069-B9EFBEED68E4}";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ddt-bde-{Guid.NewGuid():N}");
    private readonly StringWriter _console = new();
    private readonly AgentLog _log;

    public ManageBdePasswordLogTests()
    {
        Directory.CreateDirectory(_directory);
        _log = new AgentLog(new ImmediateTimeProvider(), _console);
    }

    public void Dispose()
    {
        _console.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task AnAddedPasswordReachesTheCallerOnly()
    {
        ManageBdeBitLocker bitLocker = Answering(0, "Key Protectors Added:", "    Numerical Password:", $"      ID: {Id}", "      Password:", $"        {Password}", "ACTIONS REQUIRED:", $"    {Password}");

        RecoveryPassword? added = await bitLocker.AddRecoveryPasswordAsync("C:", TestContext.Current.CancellationToken);

        Assert.Equal(Password, added?.Password);
        await AssertNotLoggedAsync();
    }

    [Fact]
    public async Task TheListingStaysOutOfTheLogs()
    {
        ManageBdeBitLocker bitLocker = Answering(0, "Volume C: []", "    Numerical Password:", $"      ID: {Id}", "      Password:", $"        {Password}");

        Assert.Equal([Guid.Parse(Id)], await bitLocker.RecoveryPasswordIdsAsync("C:", TestContext.Current.CancellationToken));
        await AssertNotLoggedAsync();
    }

    [Fact]
    public async Task AFailedAddKeepsThePasswordOutOfTheLogsAndTheError()
    {
        ManageBdeBitLocker bitLocker = Answering(5, "    Numerical Password:", $"      ID: {Id}", $"        {Password}", "ERROR: An error occurred (code 0x80310000).");

        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(() => bitLocker.AddRecoveryPasswordAsync("C:", TestContext.Current.CancellationToken));

        Assert.DoesNotContain(Password, failure.ToString(), StringComparison.Ordinal);
        await AssertNotLoggedAsync();
    }

    // A logged command drops a line that looks like a password and keeps the rest.
    [Fact]
    public async Task ALoggedCommandDropsALineThatLooksLikeAPassword()
    {
        ManageBdeBitLocker bitLocker = Answering(0, "NOTE: Encryption is already in progress.", $"    {Password}");

        await bitLocker.TurnOnAsync("C:", TestContext.Current.CancellationToken);

        List<AgentLogLine> sent = await AssertNotLoggedAsync();
        Assert.Contains(sent, line => line.Message == "NOTE: Encryption is already in progress.");
    }

    private ManageBdeBitLocker Answering(int exitCode, params string[] lines)
    {
        string script = Path.Combine(_directory, "manage-bde.cmd");
        File.WriteAllLines(script, ["@echo off", .. lines.Select(line => $"echo {line}"), $"exit /b {exitCode}"]);

        return new ManageBdeBitLocker(new ToolRunner(_log, TimeProvider.System, AnsiCodePage.Decode), script, "powershell.exe", _log, TimeProvider.System);
    }

    private async Task<List<AgentLogLine>> AssertNotLoggedAsync()
    {
        ScriptedAgentServer server = new();

        while (_log.QueuedLines > 0)
        {
            await _log.FlushAsync(server, Guid.Empty, "token", TestContext.Current.CancellationToken);
        }

        Assert.NotEmpty(server.SentLines);
        Assert.DoesNotContain(server.SentLines, line => line.Message.Contains("111111-222222", StringComparison.Ordinal));
        Assert.DoesNotContain("111111-222222", _console.ToString(), StringComparison.Ordinal);

        return server.SentLines;
    }
}

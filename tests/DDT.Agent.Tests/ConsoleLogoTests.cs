// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Agent.Consoles;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ConsoleLogoTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ddt-console-logo-").FullName;
    private readonly byte[] _png = [0x89, (byte)'P', (byte)'N', (byte)'G', .. RandomNumberGenerator.GetBytes(300)];
    private readonly ImmediateTimeProvider _time = new();
    private readonly StringWriter _output = new();

    public void Dispose()
    {
        _output.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private string Sha256 => Convert.ToHexStringLower(SHA256.HashData(_png));

    private string LogoPath => Path.Combine(_directory, $"console-logo-{Sha256[..12]}.png");

    private (ConsoleLogo Logo, ConsoleStatus Status) Create(ScriptedAgentServer server)
    {
        AgentLog log = new(_time, _output);
        ConsoleStatus status = TestAgents.Status(new ScriptedSignInPrompt { IsAvailable = false }, log);

        return (new ConsoleLogo(server, status, _directory, log), status);
    }

    [Fact]
    public async Task ShowsTheLogoItDownloadedOnce()
    {
        ScriptedAgentServer server = new() { ConsoleLogo = _png };
        (ConsoleLogo logo, ConsoleStatus status) = Create(server);

        await logo.ShowAsync(Sha256, TestContext.Current.CancellationToken);
        await logo.ShowAsync(Sha256.ToUpperInvariant(), TestContext.Current.CancellationToken);

        Assert.Equal(LogoPath, status.State.Logo);
        Assert.Equal(_png, await File.ReadAllBytesAsync(LogoPath, TestContext.Current.CancellationToken));
        Assert.Single(server.Calls, call => call == "console-logo");
    }

    // A restart of the agent in the installed Windows finds the logo it downloaded before.
    [Fact]
    public async Task TakesALogoAlreadyThere()
    {
        await File.WriteAllBytesAsync(LogoPath, _png, TestContext.Current.CancellationToken);
        ScriptedAgentServer server = new();
        (ConsoleLogo logo, ConsoleStatus status) = Create(server);

        await logo.ShowAsync(Sha256, TestContext.Current.CancellationToken);

        Assert.Equal(LogoPath, status.State.Logo);
        Assert.DoesNotContain("console-logo", server.Calls);
    }

    [Fact]
    public async Task ShowsNoneOnceTheServerHasNone()
    {
        (ConsoleLogo logo, ConsoleStatus status) = Create(new ScriptedAgentServer { ConsoleLogo = _png });

        await logo.ShowAsync(Sha256, TestContext.Current.CancellationToken);
        await logo.ShowAsync(null, TestContext.Current.CancellationToken);

        Assert.Null(status.State.Logo);
    }

    [Fact]
    public async Task NeverShowsALogoThatDoesNotMatch()
    {
        (ConsoleLogo logo, ConsoleStatus status) = Create(new ScriptedAgentServer { ConsoleLogo = [.. _png, 0] });

        await logo.ShowAsync(Sha256, TestContext.Current.CancellationToken);

        Assert.Null(status.State.Logo);
        Assert.Empty(Directory.GetFiles(_directory));
        Assert.Contains("Cannot show the logo the server has for the console (the download does not match", _output.ToString(), StringComparison.Ordinal);
    }

    // The hash becomes part of a path next to the agent.
    [Fact]
    public async Task TakesOnlyAHash()
    {
        ScriptedAgentServer server = new() { ConsoleLogo = _png };
        (ConsoleLogo logo, ConsoleStatus status) = Create(server);

        await logo.ShowAsync(@"..\..\Windows\System32\" + new string('a', 43), TestContext.Current.CancellationToken);

        Assert.Null(status.State.Logo);
        Assert.DoesNotContain("console-logo", server.Calls);
    }

    // The registration names the logo, so even a machine an administrator rejected shows it.
    [Fact]
    public async Task TheAgentShowsTheLogoItsRegistrationNames()
    {
        ScriptedAgentServer server = new ScriptedAgentServer { ConsoleLogo = _png }
            .OnRegister(_ => new AgentRegistrationResult(Guid.NewGuid(), MachineState.Rejected, null, null, 10, null, ConsoleLogoSha256: Sha256));
        (ConsoleLogo logo, ConsoleStatus status) = Create(server);
        using FakeDeploymentTools tools = new();
        AgentLog log = new(_time, TextWriter.Null);
        AgentLoop loop = new(
            server,
            new AgentMachine(new DryRunMachineIdentityReader(1), tools, new LocalRunLocator([tools.Volumes.Windows]), TestAgents.Version),
            status,
            TestAgents.Runner(server, tools, log, _time, new() { Status = status }),
            log,
            _time)
        {
            Logo = logo,
        };

        Assert.Equal(AgentExitCodes.Rejected, await loop.RunAsync(server.Stop.Token));
        Assert.Equal(LogoPath, status.State.Logo);
    }
}

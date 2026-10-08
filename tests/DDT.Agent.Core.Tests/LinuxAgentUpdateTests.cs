// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

// The agent of the Linux boot environment switches to the agent the server offers as the one in Windows PE does. Its
// file has no .exe in its name and is a program by its mode.
public sealed class LinuxAgentUpdateTests : IDisposable
{
    private const string CurrentSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly string _directory = Directory.CreateTempSubdirectory("ddt-agent-update-").FullName;
    private readonly StringWriter _console = new();

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        _console.Dispose();
    }

    private AgentUpdate Create(ScriptedAgentServer server, IAgentRelauncher relauncher, AgentEnvironment environment, params string[] arguments)
    {
        ImmediateTimeProvider time = new();
        RunningAgent agent = new(CurrentSha256, _directory, arguments) { Environment = environment };

        return new AgentUpdate(server, relauncher, new AgentLog(time, _console), time, agent);
    }

    private static (AgentRelease Release, ScriptedAgentServer Server) Offer(byte[] content)
    {
        AgentRelease release = new(Convert.ToHexStringLower(SHA256.HashData(content)), content.Length);

        return (release, new ScriptedAgentServer().OnRelease(() => release).OnDownload(content));
    }

    [Fact]
    public async Task TheNewAgentIsStartedUnderANameWithoutExeAndItsExitCodeIsPassedOn()
    {
        byte[] content = RandomNumberGenerator.GetBytes(3000);
        (AgentRelease release, ScriptedAgentServer server) = Offer(content);
        ScriptedRelauncher relauncher = new(() => AgentExitCodes.Deployed);
        AgentUpdate update = Create(server, relauncher, AgentEnvironment.LinuxBoot, "--config", "agent.json");

        Assert.Equal(AgentExitCodes.Deployed, await update.RunAsync(TestContext.Current.CancellationToken));

        (string path, IReadOnlyList<string> arguments) = Assert.Single(relauncher.Started);
        Assert.Equal(Path.Combine(_directory, $"ddt-agent-{release.Sha256[..12]}"), path);
        Assert.Equal(content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(["--config", "agent.json", AgentOptions.NoUpdateArgument], arguments);
        Assert.Equal([Path.GetFileName(path)], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    // Linux starts a file only when its mode says it is a program, and a download has no such mode by itself.
    [Fact]
    public async Task TheNewAgentIsAProgramForItsOwnerAlone()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no file modes.");

        (_, ScriptedAgentServer server) = Offer(RandomNumberGenerator.GetBytes(3000));
        ScriptedRelauncher relauncher = new(() => AgentExitCodes.Stopped);
        AgentUpdate update = Create(server, relauncher, AgentEnvironment.LinuxBoot);

        Assert.Equal(AgentExitCodes.Stopped, await update.RunAsync(TestContext.Current.CancellationToken));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(Assert.Single(relauncher.Started).Path));
        }
    }

    // An earlier download that matches is taken as it is, and it may have lost its mode on the way.
    [Fact]
    public async Task AnEarlierDownloadIsMadeAProgramAgain()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no file modes.");

        byte[] content = RandomNumberGenerator.GetBytes(3000);
        (AgentRelease release, ScriptedAgentServer server) = Offer(content);
        string path = Path.Combine(_directory, $"ddt-agent-{release.Sha256[..12]}");
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);
        AgentUpdate update = Create(server, new ScriptedRelauncher(() => AgentExitCodes.Stopped), AgentEnvironment.LinuxBoot);

        Assert.Equal(AgentExitCodes.Stopped, await update.RunAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain("download", server.Calls);

        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute));
        }
    }

    // With the real start of a process: a file that is no program of this system leaves the agent that asked going.
    [Fact]
    public async Task AFileThatCannotRunLeavesTheOldAgentGoing()
    {
        byte[] content = [0, 1, 2, 3, .. RandomNumberGenerator.GetBytes(3000)];
        (AgentRelease release, ScriptedAgentServer server) = Offer(content);
        AgentUpdate update = Create(server, new ProcessAgentRelauncher(), AgentEnvironment.LinuxBoot);

        Assert.Null(await update.RunAsync(TestContext.Current.CancellationToken));

        Assert.True(File.Exists(Path.Combine(_directory, $"ddt-agent-{release.Sha256[..12]}")));
        Assert.Contains("WARN  Cannot switch to the new agent (", _console.ToString(), StringComparison.Ordinal);
        Assert.EndsWith(
            "). Carrying on with the agent the Linux boot environment brought." + Environment.NewLine,
            _console.ToString(),
            StringComparison.Ordinal);
    }

    // 139 is what a shell makes of a program that a signal ended, and no agent ends with it.
    [Fact]
    public async Task AnAgentThatEndsWithACodeNoAgentReturnsLeavesTheOldAgentGoing()
    {
        (_, ScriptedAgentServer server) = Offer(RandomNumberGenerator.GetBytes(3000));
        AgentUpdate update = Create(server, new ScriptedRelauncher(() => 139), AgentEnvironment.LinuxBoot);

        Assert.Null(await update.RunAsync(TestContext.Current.CancellationToken));

        Assert.Contains(
            "WARN  The new agent could not run (exit code 139). Carrying on with the agent the Linux boot environment brought.",
            _console.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WindowsPEKeepsItsFileNameAndItsWords()
    {
        byte[] content = RandomNumberGenerator.GetBytes(3000);
        (AgentRelease release, ScriptedAgentServer server) = Offer(content);
        ScriptedRelauncher relauncher = new(() => unchecked((int)0xC0000135));
        AgentUpdate update = Create(server, relauncher, AgentEnvironment.WindowsPE);

        Assert.Null(await update.RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal(Path.Combine(_directory, $"ddt-agent-{release.Sha256[..12]}.exe"), Assert.Single(relauncher.Started).Path);
        Assert.Contains(
            "WARN  The new agent could not run (exit code 0xC0000135). Carrying on with the agent from the boot image.",
            _console.ToString(),
            StringComparison.Ordinal);
    }
}

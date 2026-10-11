// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.LinuxPhase;
using DDT.Agent.Sequences;
using DDT.Core.CloudInit;
using Xunit;

namespace DDT.Agent.Tests;

// The agent's removal from the installed Linux, over a folder that stands in for / as the vendor-data script and a
// run leave it, and another that stands in for the mounted seed.
public sealed class LinuxAgentRemovalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ddt-linux-removal-{Guid.NewGuid():N}");
    private readonly RecordingToolRunner _tools = new();
    private readonly StringWriter _console = new();
    private readonly AgentLog _log;
    private readonly LinuxAgentPaths _paths;

    public LinuxAgentRemovalTests()
    {
        _log = new AgentLog(new ImmediateTimeProvider(), _console);
        _paths = new LinuxAgentPaths(Path.Combine(_root, "system"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        _console.Dispose();
    }

    private string Seed => Path.Combine(_root, "seed");

    private string[] Warnings() =>
        [.. _console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Where(line => line[9..].StartsWith("WARN", StringComparison.Ordinal)).Select(line => line[15..])];

    private static void Put(string file, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    // What the script installed, what a run left, and the seed as the hand-over wrote it less the token.
    private async Task InstallAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RunFiles files = RunFiles.In(_paths.State, _log);
        await files.SaveTokenAsync("run-token-1", cancellationToken);
        await File.WriteAllTextAsync(files.StatePath, "{}", cancellationToken);
        await File.WriteAllTextAsync(files.FinalReportPath, "{}", cancellationToken);
        Put(_paths.CloudInitWaited, "");
        Put(_paths.Configuration, "{}");
        Put(_paths.LogFile);
        Put(Path.Combine(RunFiles.DirectoryIn(_paths.State), "scripts", "a.sh"), "echo a password");
        Put(Path.Combine(RunFiles.DirectoryIn(_paths.State), "packages", "left", "tool"));
        Put(_paths.Agent);
        Put(_paths.Unit);
        Put(Path.Combine(_paths.Run, LinuxRestartMarker.FileName), "");
        Directory.CreateDirectory(_paths.Shares);
        Put(Path.Combine(_paths.VendorScripts, "part-001"), VendorDataScript.Text);
        Put(Path.Combine(_paths.VendorScripts, "part-002"), "#!/bin/sh\necho the administrator's own\n");
        Put(Path.Combine(Seed, CloudInitSeed.MetaData));
        Put(Path.Combine(Seed, CloudInitSeed.UserData));
        Put(Path.Combine(Seed, CloudInitSeed.VendorData), VendorDataScript.Text);
        Put(Path.Combine(Seed, LinuxAgentFiles.SeedAgent));
        Put(Path.Combine(Seed, LinuxAgentFiles.SeedConfiguration));
        Put(Path.Combine(Seed, LinuxAgentFiles.SeedState));
    }

    private Task RemoveAsync(FolderSeeds seeds) =>
        new LinuxAgentRemoval(_paths, seeds, _tools, _log).RemoveAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task RemovesTheSeedsFilesTheStateTheUnitAndTheProgramAndLeavesWhatIsNotItsOwn()
    {
        await InstallAsync();

        await RemoveAsync(new FolderSeeds(Seed));

        // The unit is disabled while its file is still there, and systemd reads its units again once it is gone.
        Assert.Equal(["systemctl disable ddt-agent.service", "systemctl daemon-reload"], _tools.Calls);
        Assert.False(Directory.Exists(_paths.State));
        Assert.False(Directory.Exists(_paths.Program));
        Assert.False(File.Exists(_paths.Unit));
        Assert.False(Directory.Exists(_paths.Run));
        Assert.Equal([CloudInitSeed.MetaData, CloudInitSeed.UserData], Directory.GetFileSystemEntries(Seed).Select(Path.GetFileName).Order());
        Assert.Equal(["part-002"], Directory.GetFiles(_paths.VendorScripts).Select(Path.GetFileName));
        Assert.Empty(Warnings());
        Assert.EndsWith("INFO  The agent is removed from this Linux, but for what a line above says stays." + Environment.NewLine, _console.ToString(), StringComparison.Ordinal);
    }

    // A DDT server on the same system keeps its store in a folder of the same name, and that is none of the agent's.
    [Fact]
    public async Task LeavesAFolderThatHoldsWhatTheAgentDidNotPutThere()
    {
        await InstallAsync();
        Put(Path.Combine(_paths.State, "certs", "ddt-root.pem"));
        Put(Path.Combine(_paths.Program, "another program"));
        Put(Path.Combine(Seed, CloudInitSeed.VendorData), "#cloud-config\nruncmd: [true]\n");

        await RemoveAsync(new FolderSeeds(Seed));

        Assert.Equal(["certs"], Directory.GetFileSystemEntries(_paths.State).Select(Path.GetFileName));
        Assert.Equal(["another program"], Directory.GetFileSystemEntries(_paths.Program).Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(Seed, CloudInitSeed.VendorData)));
        Assert.Equal(
            [
                $"{_paths.State} holds what the agent did not put there, so the folder stays.",
                $"{_paths.Program} holds what the agent did not put there, so the folder stays.",
            ],
            Warnings());
    }

    [Fact]
    public async Task OnlyWarnsWhenTheSeedCannotBeMountedOrSystemdDoesNotAnswer()
    {
        await InstallAsync();
        _tools.Answer = (_, _) => throw new DeploymentStepException("systemctl failed with exit code 0x00000001. Its output is in the machine log.");

        await RemoveAsync(new FolderSeeds());

        Assert.False(Directory.Exists(_paths.State));
        Assert.False(File.Exists(_paths.Unit));
        Assert.False(Directory.Exists(_paths.Program));
        Assert.True(Directory.Exists(Path.Combine(Seed, LinuxAgentFiles.SeedFolder)));
        Assert.Equal(
            [
                "No cloud-init seed could be mounted, so the folder ddt and vendor-data stay on it. They hold the agent and its script.",
                "systemctl disable ddt-agent.service did not work (systemctl failed with exit code 0x00000001. Its output is in the machine log.). Run it by hand.",
                "systemctl daemon-reload did not work (systemctl failed with exit code 0x00000001. Its output is in the machine log.). Run it by hand.",
            ],
            Warnings());
    }

    // What held a secret is overwritten before it is deleted. A handle opened before sees what became of the bytes.
    [Fact]
    public async Task OverwritesTheTokenTheStateAndAScriptBeforeItDeletesThem()
    {
        await InstallAsync();
        RunFiles files = RunFiles.In(_paths.State, _log);
        string script = Path.Combine(RunFiles.DirectoryIn(_paths.State), "scripts", "a.sh");
        string seedState = Path.Combine(Seed, LinuxAgentFiles.SeedState);

        foreach (string file in (string[])[files.TokenPath, files.StatePath, files.FinalReportPath, script, seedState])
        {
            using FileStream before = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = before.Length;
            Assert.True(length > 0);

            Leftovers.Wipe(file, _log);

            byte[] left = new byte[length];
            before.ReadExactly(left);
            Assert.All(left, value => Assert.Equal(0, value));
            Assert.False(File.Exists(file));
        }
    }

    // Removing twice is what an agent does that was stopped half way, and at its next start finds nothing of a run.
    [Fact]
    public async Task ASecondRemovalFindsNothingLeftAndSaysNothingIsWrong()
    {
        await InstallAsync();
        await RemoveAsync(new FolderSeeds(Seed));

        await RemoveAsync(new FolderSeeds(Seed));

        Assert.Empty(Warnings());
    }

    [Fact]
    public async Task TheTokenRepairTakesATokenOffTheSeedAndSaysSo()
    {
        await InstallAsync();
        Put(Path.Combine(Seed, LinuxAgentFiles.SeedToken), "run-token-1");
        FolderSeeds seeds = new(Seed);

        Assert.True(new SeedTokenRepair(seeds, _log).Run());
        Assert.False(new SeedTokenRepair(seeds, _log).Run());

        Assert.False(File.Exists(Path.Combine(Seed, LinuxAgentFiles.SeedToken)));
        Assert.True(File.Exists(Path.Combine(Seed, LinuxAgentFiles.SeedState)));
        Assert.Equal([SeedTokenRepair.Repaired], Warnings());
        Assert.DoesNotContain("run-token-1", _console.ToString(), StringComparison.Ordinal);
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Net.Sockets;
using DDT.Agent.Deployment;
using DDT.Agent.LinuxPhase;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using Xunit;

namespace DDT.Agent.Tests;

// The agent's removal over what a script can leave in its working folder, with the kernel's own links, pipes and
// mounts. A folder stands in for /, and another beside it for the rest of the system, which must stay as it is.
[Trait("Category", "LinuxKernel")]
public sealed class LinuxRemovalKernelTests : IDisposable
{
    private const string Kept = "what the system keeps elsewhere";

    private static readonly Guid s_step = Guid.Parse("0193a4b2-0000-7000-8000-00000000c001");

    private readonly string _folder = Directory.CreateTempSubdirectory("ddt-linux-removal-").FullName;
    private readonly StringWriter _console = new();
    private readonly AgentLog _log;
    private readonly LibcMounts _mounts = new();
    private readonly List<string> _mounted = [];
    private readonly List<IDisposable> _held = [];
    private LoopDisk? _loop;

    public LinuxRemovalKernelTests() => _log = new AgentLog(new ImmediateTimeProvider(), _console);

    public void Dispose()
    {
        foreach (string place in _mounted)
        {
            _mounts.Unmount(place, detach: true);
        }

        foreach (IDisposable held in _held)
        {
            held.Dispose();
        }

        _loop?.Dispose();
        Directory.Delete(_folder, recursive: true);
        _console.Dispose();
    }

    private LinuxAgentPaths Paths => new(Path.Combine(_folder, "system"));

    // Where a script without a package runs, so what it leaves is here.
    private string Scripts => Path.Combine(RunFiles.DirectoryIn(Paths.State), "scripts");

    private string Outside => Path.Combine(_folder, "outside");

    // The two files of a step that the agent names itself.
    private string ScriptFile => Path.Combine(Scripts, $"{s_step:D}.sh");

    private string VariablesFile => Path.Combine(Scripts, $"{s_step:D}.variables");

    private string[] Warnings() =>
        [.. _console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Where(line => line[9..].StartsWith("WARN", StringComparison.Ordinal)).Select(line => line[15..])];

    private static string Put(string file, string content = Kept)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);

        return file;
    }

    private static void Run(string tool, params string[] arguments)
    {
        (int exitCode, string output) = LoopDisk.Run(tool, arguments);
        Assert.True(exitCode == 0, $"{tool}: {output}");
    }

    // The removal as the unit runs it. It has no stop token, so it must come back whatever the folders hold.
    private async Task RemoveAsync()
    {
        LoopDisk.SkipUnlessLinux();
        Directory.CreateDirectory(Scripts);
        LinuxAgentRemoval removal = new(Paths, new LinuxSeeds(_mounts, Paths, _log), new RecordingToolRunner(), _log);

        await Task.Run(() => removal.RemoveAsync(CancellationToken.None), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    // A FAT volume on a loop device, mounted at place: what a script left mounted.
    private void MountAt(string place)
    {
        LoopDisk.SkipUnlessLinux();
        Assert.SkipUnless(Environment.UserName == "root", "Only root may mount.");

        byte[] volume = CloudInitSeed.Build(
            CloudInitSeed.FilesOf(new CloudInitSeedFiles("instance-id: a\n", Kept, null)),
            0x1234ABCD,
            new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc),
            0);
        string file = Path.Combine(_folder, "disk.img");
        File.WriteAllBytes(file, volume);
        _loop = LoopDisk.Attach(file, partitions: 0);
        Directory.CreateDirectory(place);
        Assert.Equal(0, _mounts.Mount(_loop.Device, place, LinuxSeeds.FileSystem, LinuxSeeds.Options));
        _mounted.Add(place);
    }

    [Fact]
    public async Task ALinkToAFileElsewhereGoesAndTheFileStaysAsItIs()
    {
        LoopDisk.SkipUnlessLinux();
        string target = Put(Path.Combine(Outside, "app.conf"));
        Directory.CreateDirectory(Scripts);
        File.CreateSymbolicLink(Path.Combine(Scripts, "app.conf"), target);
        File.CreateSymbolicLink(ScriptFile, target);
        File.CreateSymbolicLink(Path.Combine(Scripts, "gone"), Path.Combine(Outside, "never there"));

        await RemoveAsync();

        Assert.Equal(Kept, File.ReadAllText(target));
        Assert.False(Directory.Exists(Paths.State), _console.ToString());
    }

    [Fact]
    public async Task ALinkToAFolderElsewhereGoesAndEverythingBelowItStays()
    {
        LoopDisk.SkipUnlessLinux();
        string file = Put(Path.Combine(Outside, "opt", "app", "bin", "tool"));
        string script = Put(Path.Combine(Outside, "opt", "app", $"{s_step:D}.sh"));
        Directory.CreateDirectory(Scripts);
        Directory.CreateSymbolicLink(Path.Combine(Scripts, "current"), Path.Combine(Outside, "opt", "app"));
        Directory.CreateSymbolicLink(Path.Combine(RunFiles.DirectoryIn(Paths.State), "packages"), Path.Combine(Outside, "opt"));

        await RemoveAsync();

        Assert.Equal(Kept, File.ReadAllText(file));
        Assert.Equal(Kept, File.ReadAllText(script));
        Assert.False(Directory.Exists(Paths.State), _console.ToString());
    }

    // A second name of a file, as pnpm and cp -al make them. Its bytes are the other name's too.
    [Fact]
    public async Task ASecondNameOfAFileElsewhereGoesAndTheFileKeepsItsBytes()
    {
        LoopDisk.SkipUnlessLinux();
        string target = Put(Path.Combine(Outside, "shared.conf"));
        Directory.CreateDirectory(Scripts);
        Run("ln", target, Path.Combine(Scripts, "shared.conf"));
        Run("ln", target, ScriptFile);
        Run("ln", target, VariablesFile);

        await RemoveAsync();

        Assert.Equal(Kept, File.ReadAllText(target));
        Assert.False(Directory.Exists(Paths.State), _console.ToString());
    }

    // Opening a pipe that nobody reads waits for ever.
    [Fact]
    public async Task APipeASocketAndADeviceGoWithoutBeingOpened()
    {
        LoopDisk.SkipUnlessLinux();
        Directory.CreateDirectory(Scripts);
        Run("mkfifo", Path.Combine(Scripts, "pipe"));
        Run("mkfifo", VariablesFile);
        Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _held.Add(socket);
        socket.Bind(new UnixDomainSocketEndPoint(Path.Combine(Scripts, "socket")));

        // A container may not let its root make a device.
        bool device = LoopDisk.Run("mknod", ScriptFile, "c", "1", "3").ExitCode == 0;

        await RemoveAsync();

        Assert.False(Directory.Exists(Paths.State), _console.ToString());
        Assert.True(device || !File.Exists(ScriptFile));
        Assert.DoesNotContain(Warnings(), warning => warning.Contains("overwritten", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFolderWithSomethingMountedBelowItStaysWithAllItHolds()
    {
        string place = Path.Combine(Scripts, "mnt", "my disk");
        MountAt(place);
        Put(ScriptFile, "echo a password");

        await RemoveAsync();

        Assert.Equal(Kept, File.ReadAllText(Path.Combine(place, CloudInitSeed.UserData)));
        Assert.Contains($"{Scripts} was not deleted, because {place} is still mounted in it and would lose its files.", Warnings());
        Assert.False(File.Exists(ScriptFile));
    }

    [Fact]
    public async Task AFolderSomethingIsMountedOnStaysWithAllItHolds()
    {
        MountAt(Scripts);

        await RemoveAsync();

        Assert.Equal(Kept, File.ReadAllText(Path.Combine(Scripts, CloudInitSeed.UserData)));
        Assert.Contains($"{Scripts} was not deleted, because a file system is mounted on it and would lose its files.", Warnings());
    }

    // /var/lib/ddt may be a link to another disk's folder. The kernel lists a mount by where it really is.
    [Fact]
    public async Task AMountIsFoundAlsoWhenTheStateFolderIsReachedThroughALink()
    {
        LoopDisk.SkipUnlessLinux();
        string real = Path.Combine(_folder, "data", "ddt");
        Directory.CreateDirectory(real);
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.State)!);
        Directory.CreateSymbolicLink(Paths.State, real);
        string place = Path.Combine(real, "scripts", "mnt");
        MountAt(place);

        await RemoveAsync();

        Assert.Equal(Kept, File.ReadAllText(Path.Combine(place, CloudInitSeed.UserData)));
        Assert.Contains($"{Scripts} was not deleted, because {place} is still mounted in it and would lose its files.", Warnings());
    }

    // Without the kernel's list nobody knows what a recursive delete would reach.
    [Fact]
    public void AFolderStaysWhenWhatIsMountedCannotBeRead()
    {
        LoopDisk.SkipUnlessLinux();
        string tool = Put(Path.Combine(_folder, "packages", "0193", "tool"));
        string package = Path.GetDirectoryName(tool)!;
        string noList = Path.Combine(_folder, "no-such-list");

        Leftovers.Delete(package, _log, directory => MountsBelow.Holding(directory, noList));

        Assert.True(File.Exists(tool));
        Assert.Null(MountsBelow.Of(package, noList));
        Assert.Equal([$"{package} was not deleted, because {MountsBelow.Unknown}."], Warnings());

        Leftovers.Delete(package, _log);

        Assert.False(Directory.Exists(package));
    }

    // A step in a loop unpacks its package again, into the folder its last turn left. That turn's script left a disk
    // mounted there, so the folder could not go with its step.
    [Fact]
    public async Task AStepVisitedAgainFailsRatherThanEmptyAFolderWithSomethingMountedInIt()
    {
        LoopDisk.SkipUnlessLinux();
        LinuxPhaseFixture linux = new();
        _held.Add(linux);
        TestZip zip = new(("install.sh", "#!/bin/sh\n"));
        RunScriptStep script = LinuxPhaseFixture.Script(1) with { PackageId = Guid.NewGuid() };
        AgentRun run = LinuxPhaseFixture.Run([script], [new AgentRunPackage(script.Id, "Tools", zip.Sha256, zip.Content.Length)]);
        await linux.HandOverAsync(run);
        string package = Path.Combine(linux.RunDirectory, "packages", $"{script.Id:D}");
        string place = Path.Combine(package, "mnt");
        MountAt(place);
        ScriptedAgentServer server = new ScriptedAgentServer()
            .OnRegister(_ => LinuxPhaseFixture.Continued())
            .OnNext(_ => LinuxPhaseFixture.Next("session-1", run))
            .ServeFile(zip.Sha256, zip.Content);

        await linux.RunAsync(server);

        Assert.Equal(Kept, File.ReadAllText(Path.Combine(place, CloudInitSeed.UserData)));
        Assert.Equal(DeploymentState.Failed, server.RunReports[^1].State);
        Assert.Contains(
            linux.Lines(),
            line => line.EndsWith(
                $"{package} is left from an earlier turn of this step and cannot be emptied for the package, because {place} is still mounted " +
                "in it and would lose its files. Unmount it, then run the step again.",
                StringComparison.Ordinal));
        Assert.DoesNotContain(linux.Tools.Calls, call => call.Contains(".sh", StringComparison.Ordinal));
    }

    // What the agent wrote and knows by name may hold a secret. What a script left is none of the run's secrets.
    [Fact]
    public async Task OverwritesTheStepsOwnFilesAndOnlyDeletesWhatElseAScriptLeft()
    {
        LoopDisk.SkipUnlessLinux();
        string run = RunFiles.DirectoryIn(Paths.State);
        string[] own = [Put(ScriptFile, "echo a password"), Put(VariablesFile, "Password=secret"), Put(Paths.LogFile, "a line")];
        string[] left =
        [
            Put(Path.Combine(Scripts, "notes.txt")),
            Put(Path.Combine(Scripts, "work", $"{s_step:D}.sh")),
            Put(Path.Combine(run, "packages", s_step.ToString("D"), "tool")),
            Put(Path.Combine(run, "cache", "0123.part")),
        ];
        List<FileStream> before = [.. own.Concat(left).Select(file => new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))];
        _held.AddRange(before);

        await RemoveAsync();

        Assert.False(Directory.Exists(Paths.State), _console.ToString());
        Assert.All(before.Take(own.Length), file => Assert.All(ReadAll(file), value => Assert.Equal(0, value)));
        Assert.All(before.Skip(own.Length), file => Assert.Equal(Kept, System.Text.Encoding.UTF8.GetString(ReadAll(file))));
    }

    // A file past the limit is deleted as it is: overwriting it would hold the removal up for what is no token.
    [Fact]
    public async Task OnlyDeletesAFileOfItsOwnThatIsLargerThanItOverwrites()
    {
        LoopDisk.SkipUnlessLinux();
        Put(ScriptFile, "echo");

        using (FileStream large = new(ScriptFile, FileMode.Open, FileAccess.Write))
        {
            large.SetLength(Leftovers.MaxWipedBytes + 1);
        }

        using FileStream before = new(ScriptFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        await RemoveAsync();

        byte[] start = new byte[4];
        before.ReadExactly(start);
        Assert.Equal("echo"u8.ToArray(), start);
        Assert.False(Directory.Exists(Paths.State), _console.ToString());
    }

    // Linux lets nobody write to a program that runs, and a script's file may still run in what the script started.
    [Fact]
    public async Task AFileThatCannotBeOpenedIsSaidAndDeleted()
    {
        LoopDisk.SkipUnlessLinux();
        Directory.CreateDirectory(Scripts);
        File.Copy("/bin/sleep", ScriptFile);
        using Process running = Process.Start(new ProcessStartInfo(ScriptFile, ["300"]))!;

        try
        {
            await RemoveAsync();
        }
        finally
        {
            running.Kill();
        }

        Assert.Contains(Warnings(), warning => warning.StartsWith($"{ScriptFile} could not be overwritten before it is deleted (", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Paths.State), _console.ToString());
    }

    private static byte[] ReadAll(FileStream file)
    {
        byte[] bytes = new byte[file.Length];
        file.ReadExactly(bytes);

        return bytes;
    }
}

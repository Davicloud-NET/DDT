// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// A run's files on an ext4 volume of their own, which writes a file's bytes where they were, as the root of a Debian
// or Ubuntu image does. The volume's every byte is searched, also where no file is any more.
[Trait("Category", "LinuxKernel")]
public sealed class LinuxRunFilesKernelTests : IDisposable
{
    private const int VolumeBytes = 32 * 1024 * 1024;

    private readonly string _folder = Directory.CreateTempSubdirectory("ddt-linux-run-files-").FullName;
    private readonly StringWriter _console = new();
    private readonly AgentLog _log;
    private readonly LibcMounts _mounts = new();
    private readonly List<string> _mounted = [];
    private LoopDisk? _loop;

    public LinuxRunFilesKernelTests() => _log = new AgentLog(new ImmediateTimeProvider(), _console);

    public void Dispose()
    {
        foreach (string place in _mounted)
        {
            _mounts.Unmount(place, detach: true);
        }

        _loop?.Dispose();
        Directory.Delete(_folder, recursive: true);
        _console.Dispose();
    }

    private string Image => Path.Combine(_folder, "root.img");

    private static string Token(int number) => $"kernel-test-run-token-{number}-0123456789abcdefghij";

    private static string Marker(int number) => $"kernel-test-state-value-{number}-0123456789";

    private static SequenceState State(int number) =>
        SequenceStates.Start(
            Guid.Parse("0193a4b2-0000-7000-8000-0000000000f1"),
            new SequenceDefinition(SequenceDefinition.CurrentVersion, [new RebootStep { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a1"), Name = "Restart" }])) with
        {
            Variables = new Dictionary<string, string>(StringComparer.Ordinal) { ["Password"] = Marker(number) },
        };

    // The mounted volume's folder. Skips where the system makes no ext4 or mounts none.
    private string Volume()
    {
        LoopDisk.SkipUnlessLinux();
        Assert.SkipUnless(Environment.UserName == "root", "Only root may mount.");

        using (FileStream image = new(Image, FileMode.CreateNew, FileAccess.Write))
        {
            image.SetLength(VolumeBytes);
        }

        (int exitCode, string output) = LoopDisk.Run("mkfs.ext4", "-q", "-F", Image);
        Assert.SkipUnless(exitCode == 0, $"This system makes no ext4 file system ({output}).");
        _loop = LoopDisk.Attach(Image, partitions: 0);
        string place = Path.Combine(_folder, "root");
        Directory.CreateDirectory(place);
        int error = _mounts.Mount(_loop.Device, place, "ext4", string.Empty);
        Assert.SkipUnless(error == 0, $"This system mounts no ext4 file system (error {error}).");
        _mounted.Add(place);

        return place;
    }

    // What the volume holds once everything written is on it, also in blocks no file owns. Only this volume is
    // written out: the system's other file systems are none of the test's.
    private byte[] Raw()
    {
        Assert.Equal(0, LoopDisk.Run("sync", "-f", Path.Combine(_folder, "root")).ExitCode);

        return File.ReadAllBytes(Image);
    }

    private static bool Holds(byte[] bytes, string text) => bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;

    [Fact]
    public async Task EverySaveLeavesNoByteOfTheTokenAndTheStateItReplaced()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RunFiles files = RunFiles.In(Volume(), _log);

        for (int number = 1; number <= 5; number++)
        {
            await files.SaveTokenAsync(Token(number), cancellationToken);
            await files.SaveStateAsync(State(number), cancellationToken);
        }

        byte[] during = Raw();

        // The search finds what a file still holds, so it would find the earlier ones too.
        Assert.True(Holds(during, Token(5)));
        Assert.True(Holds(during, Marker(5)));
        Assert.All(Enumerable.Range(1, 4), number => Assert.False(Holds(during, Token(number)), $"token {number}"));
        Assert.All(Enumerable.Range(1, 4), number => Assert.False(Holds(during, Marker(number)), $"state {number}"));

        files.Discard();
        byte[] after = Raw();

        Assert.All(Enumerable.Range(1, 5), number => Assert.False(Holds(after, Token(number)), $"token {number}"));
        Assert.All(Enumerable.Range(1, 5), number => Assert.False(Holds(after, Marker(number)), $"state {number}"));
        Assert.DoesNotContain("WARN", _console.ToString(), StringComparison.Ordinal);
    }

    // The power went between two saves' steps: one left its temporary file, another the file it had replaced.
    [Fact]
    public async Task TheNextStartOverwritesWhatASaveThatWasCutOffLeft()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RunFiles files = RunFiles.In(Volume(), _log);
        await files.SaveTokenAsync(Token(3), cancellationToken);
        await File.WriteAllTextAsync(files.TokenPath + FileReplacement.Temporary, Token(4), cancellationToken);
        await File.WriteAllTextAsync(files.TokenPath + FileReplacement.Replaced, Token(2), cancellationToken);
        await File.WriteAllTextAsync(files.StatePath + FileReplacement.Temporary, Marker(1), cancellationToken);
        Assert.True(Holds(Raw(), Token(2)));

        files.Recover();

        byte[] raw = Raw();
        Assert.Equal(Token(3), await files.LoadTokenAsync(cancellationToken));
        Assert.True(Holds(raw, Token(3)));
        Assert.All((string[])[Token(2), Token(4), Marker(1)], text => Assert.False(Holds(raw, text), text));
        Assert.Equal(["token"], Directory.EnumerateFiles(Path.GetDirectoryName(files.TokenPath)!).Select(Path.GetFileName));
    }

    // The power went after the old file got its second name and before the new file took its place. That second name
    // shares its bytes with the file the run still needs.
    [Fact]
    public async Task ASecondNameOfTheFileItselfIsTakenAwayWithoutTouchingItsBytes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RunFiles files = RunFiles.In(Volume(), _log);
        await files.SaveTokenAsync(Token(1), cancellationToken);
        await files.SaveStateAsync(State(1), cancellationToken);
        Assert.Equal(0, LoopDisk.Run("ln", files.TokenPath, files.TokenPath + FileReplacement.Replaced).ExitCode);
        Assert.Equal(0, LoopDisk.Run("ln", files.StatePath, files.StatePath + FileReplacement.Replaced).ExitCode);

        files.Recover();

        Assert.Equal(Token(1), await files.LoadTokenAsync(cancellationToken));
        Assert.Equal(Marker(1), (await files.LoadStateAsync(cancellationToken))?.Variables["Password"]);
        Assert.Equal(["state.json", "token"], Directory.EnumerateFiles(Path.GetDirectoryName(files.TokenPath)!).Select(Path.GetFileName).Order());

        // And the next save replaces it as every save does.
        await files.SaveTokenAsync(Token(2), cancellationToken);

        Assert.Equal(Token(2), await files.LoadTokenAsync(cancellationToken));
        Assert.False(Holds(Raw(), Token(1)));
    }
}

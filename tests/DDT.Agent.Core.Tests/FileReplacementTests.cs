// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// No test can cut the power, so a save's steps are taken on a disk that keeps what a power loss would leave after each.
public sealed class FileReplacementTests
{
    private static readonly string s_path = Path.Combine("run", "token");
    private static readonly byte[] s_old = Encoding.ASCII.GetBytes("old-token");
    private static readonly byte[] s_new = Encoding.ASCII.GetBytes("new-token");

    private static string Name(byte[] content) =>
        content.SequenceEqual(s_old) ? "old" : content.SequenceEqual(s_new) ? "new" : content.All(value => value == 0) ? "zeros" : "other";

    // What the file may hold after a power loss right now. "none" where no file has the name.
    private static string File(JournaledDisk disk) =>
        string.Join(" or ", disk.AfterPowerLoss().Select(way => way.TryGetValue("token", out byte[]? content) ? Name(content) : "none").Distinct(StringComparer.Ordinal));

    [Fact]
    public async Task APowerLossAfterAnyStepOfASaveLeavesTheOldFileOrTheNewOne()
    {
        JournaledDisk disk = new();
        disk.Put(s_path, s_old);
        List<string> left = [];
        disk.AfterStep = step => left.Add($"{step}: {File(disk)}");

        await FileReplacement.ReplaceAsync(s_path, s_new, disk, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "flush run: old",
                "wipe token.tmp: old",
                "wipe token.old: old",
                "write token.tmp: old",
                "link token token.old: old",
                "rename token.tmp token: old or new",
                "flush run: new",
                "wipe token.old: new",
            ],
            left);
        Assert.Equal(["token"], disk.AfterPowerLoss()[^1].Keys);
    }

    [Fact]
    public async Task TheOldCopyIsNotOverwrittenWhereTheFolderCannotBeWrittenOut()
    {
        JournaledDisk disk = new() { Flushes = false };
        disk.Put(s_path, s_old);
        List<string> left = [];
        disk.AfterStep = step => left.Add(File(disk));

        await FileReplacement.ReplaceAsync(s_path, s_new, disk, TestContext.Current.CancellationToken);

        Assert.Equal("delete token.old", disk.Steps[^1]);
        Assert.All(left, file => Assert.DoesNotContain("zeros", file, StringComparison.Ordinal));
    }

    // On the real disk, where only Linux has a folder to write out. Said once: a save comes every few seconds.
    [Fact]
    public void AFolderThatCannotBeWrittenOutIsSaidOnceWithWhatFollowsFromIt()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux writes a folder out.");
        using StringWriter console = new();
        ReplacementDisk disk = new(new AgentLog(new ImmediateTimeProvider(), console));
        string missing = Path.Combine(Path.GetTempPath(), $"ddt-no-folder-{Guid.NewGuid():N}");

        Assert.False(disk.Flush(missing));
        Assert.False(disk.Flush(missing));

        string said = Assert.Single(console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains($"WARN  The folder {missing} cannot be written out to the disk (", said, StringComparison.Ordinal);
        Assert.EndsWith(
            "The copy a save replaces is therefore not overwritten, and its bytes stay on the disk until something else is written there.",
            said,
            StringComparison.Ordinal);
    }

    // The save before it was cut off between its rename and the folder's way to the disk.
    [Fact]
    public async Task ASaveAfterOneThatWasCutOffWritesTheFolderOutBeforeItOverwritesWhatThatOneLeft()
    {
        JournaledDisk disk = new();
        disk.Put(s_path, s_old);
        await disk.WriteNewAsync(s_path + FileReplacement.Temporary, s_new, TestContext.Current.CancellationToken);
        disk.Link(s_path, s_path + FileReplacement.Replaced);
        disk.Rename(s_path + FileReplacement.Temporary, s_path);
        List<string> left = [];
        disk.AfterStep = step => left.Add(File(disk));

        await FileReplacement.ReplaceAsync(s_path, Encoding.ASCII.GetBytes("newest"), disk, TestContext.Current.CancellationToken);

        Assert.All(left, file => Assert.DoesNotContain("zeros", file, StringComparison.Ordinal));
        Assert.Equal("other", left[^1]);
    }

    [Fact]
    public async Task AFirstSaveHasNoOldCopyToOverwrite()
    {
        JournaledDisk disk = new();

        await FileReplacement.ReplaceAsync(s_path, s_new, disk, TestContext.Current.CancellationToken);

        Assert.Equal(["flush run", "wipe token.tmp", "wipe token.old", "write token.tmp", "link token token.old", "rename token.tmp token", "flush run"], disk.Steps);
        Assert.Equal("new", File(disk));
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.LinuxPhase;
using Xunit;

namespace DDT.Agent.Tests;

// systemd names the stream it connected to the journal in JOURNAL_STREAM. The variable stays set for a process whose
// output was replaced since, so only a stream with that device and inode is the journal's.
public sealed class JournalStreamTests
{
    [Theory]
    [InlineData("22:4711", true)]
    [InlineData("22:4712", false)]
    [InlineData("23:4711", false)]
    [InlineData("22", false)]
    [InlineData("22:4711:1", false)]
    [InlineData("22:", false)]
    [InlineData("x:y", false)]
    [InlineData("-22:4711", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheStreamTheVariableNamesByDeviceAndInodeIsTheJournals(string? value, bool named)
    {
        Assert.Equal(named, JournalStream.Names(value, (22, 4711)));
    }

    [Fact]
    public void AStreamTheKernelSaysNothingOfIsNotTheJournals()
    {
        Assert.False(JournalStream.Names("22:4711", null));
    }

    // The variable is not there when the agent is started by hand, and names a stream that is not there in a test.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0:1")]
    public void AnOutputTheVariableDoesNotNameIsNotTheJournals(string? value)
    {
        Assert.False(JournalStream.IsOutput(value));
    }

    // stat prints st_dev and st_ino in decimal, which is how systemd writes the variable.
    [Fact]
    public void ReadsAnOpenFilesDeviceAndInodeAsStatPrintsThem()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "This test calls the Linux kernel.");
        string path = Path.Combine(Path.GetTempPath(), $"ddt-journal-stream-{Guid.NewGuid():N}");
        File.WriteAllText(path, "line");

        try
        {
            (int exitCode, string printed) = LoopDisk.Run("stat", "-c", "%d:%i", path);
            Assert.SkipUnless(exitCode == 0, "This system has no stat that prints a device and an inode.");
            using FileStream file = File.OpenRead(path);
            using FileStream other = File.OpenRead("/dev/null");

            Assert.True(JournalStream.Names(printed, JournalStream.Of(file.SafeFileHandle)));
            Assert.False(JournalStream.Names(printed, JournalStream.Of(other.SafeFileHandle)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

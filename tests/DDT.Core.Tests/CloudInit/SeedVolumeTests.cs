// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using Xunit;

namespace DDT.Core.Tests.CloudInit;

// A seed's volume read back as a disk holds it, so a hand-over can build it again with every file it has by then.
public sealed class SeedVolumeTests
{
    private const uint Serial = 0x26A1C0DE;

    private static readonly DateTime s_written = new(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc);

    private static readonly CloudInitSeedFiles s_files = new("instance-id: a\n", "#cloud-config\nusers:\n  - default\n", "version: 2\n");

    private static SeedFile Text(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));

    private static byte[] Build(params SeedFile[] files) => CloudInitSeed.Build(files, Serial, s_written, 2048);

    private static IEnumerable<(string Path, string Text)> Said(IEnumerable<SeedFile> files) =>
        files.Select(file => (file.Path, Encoding.UTF8.GetString(file.Content)));

    [Fact]
    public void ReadsEveryFileByItsPathInTheVolumesOrderAndItsSerialNumber()
    {
        SeedFile[] files = [.. CloudInitSeed.FilesOf(s_files), Text("extra/Notes.TXT", "one"), Text("extra/deeper/two.cfg", "two"), Text("LAST", "")];

        SeedVolume read = SeedVolume.Read(Build(files));

        Assert.Equal(Serial, read.SerialNumber);
        Assert.Equal(Said(files), Said(read.Files));
    }

    // A volume built again from what was read is the volume that was read, byte for byte.
    [Fact]
    public void WhatItReadsBuildsTheSameVolumeAgain()
    {
        byte[] volume = CloudInitSeed.Build(s_files, Serial, s_written, 2048);
        SeedVolume read = SeedVolume.Read(volume);

        Assert.Equal(volume, CloudInitSeed.Build(read.Files, read.SerialNumber, s_written, 2048));
        Assert.Null(read.ChangesSince(CloudInitSeed.FilesOf(s_files)));
    }

    [Fact]
    public void SaysWhichFilesWereChangedAddedAndRemovedSinceTheSeedWasWritten()
    {
        IReadOnlyList<SeedFile> written = CloudInitSeed.FilesOf(s_files);
        SeedVolume same = SeedVolume.Read(Build([.. written]));
        SeedVolume changed = SeedVolume.Read(Build(written[0], Text(CloudInitSeed.UserData, "#cloud-config\n"), Text("extra/notes.txt", "kept")));

        Assert.Null(same.ChangesSince(written));
        Assert.Equal("user-data was changed, extra/notes.txt was added, network-config was removed", changed.ChangesSince(written));

        // FAT names ignore case, so a file written back under another case is the same file.
        Assert.Null(same.ChangesSince([.. written.Select(file => file with { Path = file.Path.ToUpperInvariant() })]));
    }

    [Fact]
    public void RefusesWhatIsNoFatVolumeLabelledCidata()
    {
        byte[] other = new FatVolumeBuilder(CloudInitSeed.SizeBytes, "DATA", 7, s_written) { Type = FatType.Fat16 }.Build();

        Assert.Equal("The partition holds no FAT file system.", Assert.Throws<InvalidDataException>(() => SeedVolume.Read(new byte[CloudInitSeed.SizeBytes])).Message);
        Assert.Equal("The volume is labelled DATA.", Assert.Throws<InvalidDataException>(() => SeedVolume.Read(other)).Message);
    }

    // The builder makes a folder for the files in it, so an empty one could not be kept.
    [Fact]
    public void RefusesAnEmptyFolderAndFoldersNestedWithoutEnd()
    {
        byte[] volume = Build(Text(CloudInitSeed.UserData, "#cloud-config\n"), Text("NOTES/A.TXT", "gone"));
        int entry = volume.AsSpan().IndexOf("A       TXT"u8);
        volume[entry] = 0xE5;

        Assert.Equal(
            "The volume holds the empty folder NOTES, which cannot be kept.",
            Assert.Throws<InvalidDataException>(() => SeedVolume.Read(volume)).Message);
        Assert.Equal(
            "The volume's folders are nested more than 8 deep.",
            Assert.Throws<InvalidDataException>(() => SeedVolume.Read(Build(Text("a/b/c/d/e/f/g/h/i/j/file", "deep")))).Message);
        Assert.Single(SeedVolume.Read(Build(Text("a/b/c/d/e/f/g/h/file", "fits"))).Files);
    }

    [Theory]
    [InlineData("vendor-data", true)]
    [InlineData("VENDOR-DATA", true)]
    [InlineData("ddt", true)]
    [InlineData("ddt/token", true)]
    [InlineData("DDT/notes.txt", true)]
    [InlineData("ddt-notes.txt", false)]
    [InlineData("user-data", false)]
    [InlineData("extra/vendor-data", false)]
    public void KnowsThePathsTheHandOverNeedsForItself(string path, bool needed)
    {
        Assert.Equal(needed, LinuxAgentFiles.IsForSeed(path));
    }
}

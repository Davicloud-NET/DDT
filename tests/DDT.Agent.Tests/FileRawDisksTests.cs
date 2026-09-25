// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// The dry run's disk is a file beside its root, which the next clean deletes.
public sealed class FileRawDisksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ddt-dry-run-test-{Guid.NewGuid():N}");
    private readonly AgentLog _log = new(new ImmediateTimeProvider(), TextWriter.Null);

    public void Dispose() => File.Delete(FileRawDisks.PathFor(_root, 0));

    [Fact]
    public void StandsInForTheWholeDiskInSectorsOf512Bytes()
    {
        LocalDisk disk = FakeDeploymentTools.Disk(0, sizeBytes: 64L * 1024 * 1024);
        byte[] sector = Enumerable.Repeat((byte)0x5A, 512).ToArray();

        using (IRawDisk raw = new FileRawDisks(_root, _log).Open(disk))
        {
            Assert.Equal((512, disk.SizeBytes), (raw.SectorSize, raw.Length));
            raw.Write(disk.SizeBytes - 512, sector);
            Assert.Throws<ArgumentException>(() => raw.Write(100, sector));
            Assert.Throws<ArgumentException>(() => raw.Write(disk.SizeBytes, sector));
            raw.Flush();
        }

        string path = FileRawDisks.PathFor(_root, 0);
        Assert.Equal($"{_root}-disk0.img", path);
        Assert.Equal(disk.SizeBytes, new FileInfo(path).Length);

        using IRawDisk again = new FileRawDisks(_root, _log).Open(disk);
        byte[] read = new byte[512];
        again.Read(disk.SizeBytes - 512, read);
        Assert.Equal(sector, read);
    }

    [Fact]
    public async Task TheDryRunsCleanDeletesTheDisk()
    {
        LocalDisk disk = FakeDeploymentTools.Disk(0, sizeBytes: 1024 * 1024);
        new FileRawDisks(_root, _log).Open(disk).Dispose();

        IReadOnlyList<Guid> erased = await new DryRunDiskPartitioner(_root, _log).CleanAsync(disk, TestContext.Current.CancellationToken);

        Assert.Empty(erased);
        Assert.False(File.Exists(FileRawDisks.PathFor(_root, 0)));
    }
}

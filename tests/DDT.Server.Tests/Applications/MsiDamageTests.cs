// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Applications.Installers;
using Xunit;

namespace DDT.Server.Tests.Applications;

// A damaged or hostile file is not readable: null, never an exception or a loop.
public sealed class MsiDamageTests
{
    private const int SectorSize = 512;

    [Fact]
    public void ALoopedSectorChainIsNotReadable()
    {
        byte[] msi = TestMsi.Create(new TestMsiOptions { Properties = TestMsi.Product(), EmptyStringsFirst = 2000 });
        Assert.NotNull(Read(msi));
        uint start = TestCompoundFile.StartSector(msi, SectorSize, 2);
        TestCompoundFile.SetFatEntry(msi, SectorSize, start, start);

        Assert.Null(Read(msi));
    }

    [Fact]
    public void AChainThatLeavesTheFileIsNotReadable()
    {
        byte[] msi = TestMsi.Create(new TestMsiOptions { Properties = TestMsi.Product(), EmptyStringsFirst = 2000 });
        Assert.NotNull(Read(msi));
        uint start = TestCompoundFile.StartSector(msi, SectorSize, 2);
        TestCompoundFile.SetFatEntry(msi, SectorSize, start, 0x00FFFFFF);

        Assert.Null(Read(msi));
    }

    [Fact]
    public void ADirectoryTreeWithACycleIsNotReadable()
    {
        byte[] msi = TestMsi.Create(new TestMsiOptions { Properties = TestMsi.Product() });
        Assert.NotNull(Read(msi));
        TestCompoundFile.SetRightSibling(msi, SectorSize, 3, 1);

        Assert.Null(Read(msi));
    }

    [Fact]
    public void ATruncatedFileIsReadWholeOrNotAtAll()
    {
        byte[] msi = TestMsi.Fixture();
        MsiProperties? whole = Read(msi);

        for (int length = 0; length < msi.Length; length += 97)
        {
            MsiProperties? truncated = Read(msi[..length]);

            Assert.True(truncated is null || truncated == whole, $"Cut at {length}");
        }

        Assert.Null(Read(msi[..(msi.Length / 2)]));
    }

    [Fact]
    public void ACompoundFileWithoutMsiTablesIsNotAnMsi()
    {
        byte[] document = TestCompoundFile.Create([("WordDocument", new byte[600]), ("\u0005SummaryInformation", new byte[48])]);

        Assert.Null(Read(document));
    }

    [Fact]
    public void AnUnknownCodePageIsNotReadable()
    {
        (string Name, byte[] Data)[] streams = TestMsi.Streams(new TestMsiOptions { Properties = TestMsi.Product() });
        streams[1].Data[0] = 0x39;
        streams[1].Data[1] = 0x30;

        Assert.Null(Read(TestCompoundFile.Create(streams)));
    }

    [Fact]
    public void AStringPoolLongerThanItsDataIsNotReadable()
    {
        (string Name, byte[] Data)[] streams = TestMsi.Streams(new TestMsiOptions { Properties = TestMsi.Product() });
        streams[2] = (streams[2].Name, streams[2].Data[..10]);

        Assert.Null(Read(TestCompoundFile.Create(streams)));
    }

    [Fact]
    public void RandomDamageNeverThrows()
    {
        byte[] original = TestMsi.Fixture();
        Random random = new(20261111);

        for (int round = 0; round < 2000; round++)
        {
            byte[] damaged = (byte[])original.Clone();

            for (int flip = 0; flip < 1 + random.Next(8); flip++)
            {
                damaged[random.Next(damaged.Length)] = (byte)random.Next(256);
            }

            _ = Read(damaged);
        }
    }

    [Fact]
    public void BytesThatAreNoCompoundFileAreNotReadable()
    {
        Assert.Null(Read([]));
        Assert.Null(Read([0xD0, 0xCF, 0x11, 0xE0]));
        Assert.Null(Read(new byte[4096]));
    }

    private static MsiProperties? Read(byte[] msi)
    {
        using MemoryStream stream = new(msi);

        return MsiProperties.Read(stream);
    }
}

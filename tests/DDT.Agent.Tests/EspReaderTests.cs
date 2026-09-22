// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class EspReaderTests
{
    private const string SystemRoot = @"S:\";

    private static readonly Guid s_partitionId = Guid.Parse("5e2b1a3c-4d6f-4a8b-9c0d-1e2f3a4b5c6d");
    private static readonly Guid s_basicDataType = Guid.Parse("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7");

    [Theory]
    [InlineData(512u, 2048ul, 204800ul)]
    [InlineData(4096u, 256ul, 25600ul)]
    public void ReadsWhereThePartitionLiesInLogicalBlocks(uint bytesPerSector, ulong startLba, ulong sizeLba)
    {
        // 1 MiB from the start of the disk, 100 MiB long.
        byte[] information = PartitionInformation(DriveLayoutReader.StyleGpt, DriveLayoutReader.EfiSystemPartitionType);

        EspPartition esp = EspReader.Parse(SystemRoot, information, Geometry(bytesPerSector));

        Assert.Equal(new EspPartition(1, startLba, sizeLba, s_partitionId), esp);
    }

    [Fact]
    public void RefusesAPartitionOnAnMbrDisk()
    {
        byte[] information = PartitionInformation(DriveLayoutReader.StyleMbr, DriveLayoutReader.EfiSystemPartitionType);

        DeploymentStepException exception = Assert.Throws<DeploymentStepException>(() => EspReader.Parse(SystemRoot, information, Geometry(512)));
        Assert.Equal(@"S:\ is not an EFI system partition on a GPT disk, so no firmware boot entry can point at it.", exception.Message);
    }

    [Fact]
    public void RefusesAPartitionThatIsNoEfiSystemPartition()
    {
        byte[] information = PartitionInformation(DriveLayoutReader.StyleGpt, s_basicDataType);

        Assert.Throws<DeploymentStepException>(() => EspReader.Parse(SystemRoot, information, Geometry(512)));
    }

    [Fact]
    public void RefusesAnswersThatAreTooShort()
    {
        byte[] information = PartitionInformation(DriveLayoutReader.StyleGpt, DriveLayoutReader.EfiSystemPartitionType);

        Assert.Throws<DeploymentStepException>(() => EspReader.Parse(SystemRoot, information.AsSpan(0, 64), Geometry(512)));
        Assert.Throws<DeploymentStepException>(() => EspReader.Parse(SystemRoot, information, Geometry(512).AsSpan(0, 20)));
    }

    [Fact]
    public void ReadsTheUniqueGuidOfAnyGptPartition() =>
        Assert.Equal(s_partitionId, PartitionReader.ParseId(@"W:\", PartitionInformation(DriveLayoutReader.StyleGpt, s_basicDataType)));

    [Fact]
    public void AnMbrPartitionHasNoGuidToFindItAgain()
    {
        DeploymentStepException exception = Assert.Throws<DeploymentStepException>(
            () => PartitionReader.ParseId(@"W:\", PartitionInformation(DriveLayoutReader.StyleMbr, s_basicDataType)));

        Assert.Equal(@"W:\ is not on a GPT disk, so its partition cannot be found again after a restart.", exception.Message);
    }

    [Fact]
    public void RefusesAVolumeThatIsNoDrive()
    {
        DeploymentStepException exception = Assert.Throws<DeploymentStepException>(() => PartitionReader.ReadId(Path.GetTempPath()));

        Assert.EndsWith(" is not a drive, so its partition cannot be read.", exception.Message, StringComparison.Ordinal);
    }

    // PARTITION_INFORMATION_EX: the style, then StartingOffset at 8, PartitionLength at 16 and PartitionNumber at 24,
    // then for GPT the type GUID at 32 and the unique GUID at 48.
    private static byte[] PartitionInformation(int style, Guid type)
    {
        byte[] information = new byte[EspReader.PartitionInformationLength];
        BinaryPrimitives.WriteInt32LittleEndian(information, style);
        BinaryPrimitives.WriteInt64LittleEndian(information.AsSpan(8), 1024L * 1024);
        BinaryPrimitives.WriteInt64LittleEndian(information.AsSpan(16), 100L * 1024 * 1024);
        BinaryPrimitives.WriteUInt32LittleEndian(information.AsSpan(24), 1);
        Assert.True(type.TryWriteBytes(information.AsSpan(32)));
        Assert.True(s_partitionId.TryWriteBytes(information.AsSpan(48)));

        return information;
    }

    // DISK_GEOMETRY: Cylinders, MediaType, TracksPerCylinder, SectorsPerTrack and BytesPerSector at 20, each set so
    // none can pass for another.
    private static byte[] Geometry(uint bytesPerSector)
    {
        byte[] geometry = new byte[EspReader.GeometryLength];
        BinaryPrimitives.WriteInt64LittleEndian(geometry, 30401);
        BinaryPrimitives.WriteInt32LittleEndian(geometry.AsSpan(8), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(geometry.AsSpan(12), 255);
        BinaryPrimitives.WriteUInt32LittleEndian(geometry.AsSpan(16), 63);
        BinaryPrimitives.WriteUInt32LittleEndian(geometry.AsSpan(20), bytesPerSector);

        return geometry;
    }
}

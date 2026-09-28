// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Agent.Facts;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class ProcessorCountTests
{
    private const uint RelationProcessorCore = 0;
    private const uint RelationCache = 2;

    // A SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX as Windows lays it out on x64, one GROUP_AFFINITY per mask.
    private static byte[] Record(uint relationship, ushort group, params ulong[] masks)
    {
        byte[] record = new byte[32 + (masks.Length * 16)];
        BinaryPrimitives.WriteUInt32LittleEndian(record, relationship);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)record.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(30), (ushort)masks.Length);

        for (int index = 0; index < masks.Length; index++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(32 + (index * 16)), masks[index]);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(40 + (index * 16)), group);
        }

        return record;
    }

    private static byte[] Core(ulong mask, ushort group = 0) => Record(RelationProcessorCore, group, mask);

    [Fact]
    public void CountsCoresAndTheirLogicalProcessors()
    {
        byte[] records = [.. Core(0b11), .. Core(0b1100), .. Core(0b110000), .. Core(0b11000000)];

        Assert.Equal(new ProcessorCount(4, 8), ProcessorCount.From(records));
    }

    // Efficiency cores of a hybrid processor have one logical processor each.
    [Fact]
    public void CountsCoresWithAndWithoutSimultaneousMultithreading()
    {
        byte[] records = [.. Core(0b11), .. Core(0b100), .. Core(0b1000)];

        Assert.Equal(new ProcessorCount(3, 4), ProcessorCount.From(records));
    }

    [Fact]
    public void CountsTheCoresOfEveryProcessorGroup()
    {
        byte[] records = [.. Core(0b11, group: 0), .. Core(0b11, group: 1), .. Record(RelationProcessorCore, 0, 0b1, 0b1)];

        Assert.Equal(new ProcessorCount(3, 6), ProcessorCount.From(records));
    }

    [Fact]
    public void SkipsRecordsOfOtherRelationships()
    {
        byte[] records = [.. Record(RelationCache, 0, 0b1111), .. Core(0b11)];

        Assert.Equal(new ProcessorCount(1, 2), ProcessorCount.From(records));
    }

    [Fact]
    public void CountsNothingWithoutCores()
    {
        Assert.Null(ProcessorCount.From([]));
        Assert.Null(ProcessorCount.From(Record(RelationCache, 0, 0b1)));
    }

    [Fact]
    public void RefusesRecordsThatAreNotWellFormed()
    {
        byte[] core = Core(0b11);

        byte[] noSize = [.. core];
        BinaryPrimitives.WriteUInt32LittleEndian(noSize.AsSpan(4), 0);
        Assert.Null(ProcessorCount.From(noSize));

        byte[] pastTheEnd = [.. core];
        BinaryPrimitives.WriteUInt32LittleEndian(pastTheEnd.AsSpan(4), 64);
        Assert.Null(ProcessorCount.From(pastTheEnd));

        byte[] tooManyGroups = [.. core];
        BinaryPrimitives.WriteUInt16LittleEndian(tooManyGroups.AsSpan(30), 2);
        Assert.Null(ProcessorCount.From(tooManyGroups));

        byte[] tooShort = [.. core[..24]];
        BinaryPrimitives.WriteUInt32LittleEndian(tooShort.AsSpan(4), 24);
        Assert.Null(ProcessorCount.From(tooShort));
    }
}

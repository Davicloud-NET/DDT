// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Numerics;

namespace DDT.Agent.Facts;

// The machine's cores and logical processors, counted in what GetLogicalProcessorInformationEx writes for
// RelationProcessorCore: a SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX per core, each with the affinity masks of its logical
// processors. The masks see every processor group, where Environment.ProcessorCount sees only the agent's own affinity.
public sealed record ProcessorCount(int Cores, int LogicalProcessors)
{
    // Relationship and Size, then PROCESSOR_RELATIONSHIP at 8: Flags, EfficiencyClass and 20 reserved bytes, GroupCount at
    // 30 and the GROUP_AFFINITY array at 32, each a 64-bit mask, the group and three reserved words.
    private const int HeaderLength = 8;
    private const int GroupCountOffset = 30;
    private const int GroupMaskOffset = 32;
    private const int GroupAffinityLength = 16;

    // Null for no cores, and for records that are not well formed, which count nothing safely.
    public static ProcessorCount? From(ReadOnlySpan<byte> records)
    {
        int cores = 0;
        int logical = 0;
        int offset = 0;

        while (offset + HeaderLength <= records.Length)
        {
            uint relationship = BinaryPrimitives.ReadUInt32LittleEndian(records[offset..]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(records[(offset + 4)..]);

            if (size < HeaderLength || size > records.Length - offset)
            {
                return null;
            }

            ReadOnlySpan<byte> record = records.Slice(offset, (int)size);

            if (relationship == NativeMethods.RelationProcessorCore)
            {
                if (record.Length < GroupMaskOffset)
                {
                    return null;
                }

                int groups = BinaryPrimitives.ReadUInt16LittleEndian(record[GroupCountOffset..]);

                if (GroupMaskOffset + (groups * GroupAffinityLength) > record.Length)
                {
                    return null;
                }

                for (int group = 0; group < groups; group++)
                {
                    logical += BitOperations.PopCount(BinaryPrimitives.ReadUInt64LittleEndian(record[(GroupMaskOffset + (group * GroupAffinityLength))..]));
                }

                cores++;
            }

            offset += (int)size;
        }

        return cores == 0 ? null : new ProcessorCount(cores, Math.Max(logical, cores));
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;

namespace DDT.Core.Sequences;

// The most disk space any path through a sequence needs, counting every step that may run: partitions, files and a
// cloud-init seed once. An IF takes the larger branch; a repeat's body counts once, as each time round is the same.
public static class SequenceSizes
{
    private const long Megabyte = 1024 * 1024;

    // The Microsoft reserved partition every Partition step makes.
    public const long ReservedPartitionBytes = 16 * Megabyte;

    // FileBytes gives what a step's files take on the disk once downloaded and unpacked, such as an image's download
    // and the Windows it expands to; the caller knows the files, which the definition only names.
    public static long RequiredBytes(SequenceDefinition definition, Func<SequenceStep, long> fileBytes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(fileBytes);

        Paths paths = Series(definition.Steps ?? [], fileBytes);

        return Math.Max(paths.Plain ?? 0, paths.Seeded is { } seeded ? seeded + CloudInitSeed.DiskBytes : 0);
    }

    public static long PartitionBytes(PartitionStep partition)
    {
        ArgumentNullException.ThrowIfNull(partition);

        return ((long)partition.SystemPartitionMegabytes + partition.RecoveryPartitionMegabytes) * Megabyte + ReservedPartitionBytes;
    }

    private static Paths Series(IReadOnlyList<SequenceStep?> steps, Func<SequenceStep, long> fileBytes)
    {
        Paths paths = new(0, null);

        foreach (SequenceStep? step in steps)
        {
            if (step is not null)
            {
                paths = paths.Then(Node(step, fileBytes));
            }
        }

        return paths;
    }

    private static Paths Node(SequenceStep step, Func<SequenceStep, long> fileBytes)
    {
        if (!step.IsContainer)
        {
            long bytes = fileBytes(step) + (step is PartitionStep partition ? PartitionBytes(partition) : 0);

            return step is WriteCloudInitSeedStep ? new Paths(null, bytes) : new Paths(bytes, null);
        }

        IEnumerable<Paths> bodies = step.Bodies.Select(body => Series(body.Steps ?? [], fileBytes));

        return step is IfStep
            ? bodies.Aggregate(Paths.None, (either, body) => either.Or(body))
            : bodies.Aggregate(new Paths(0, null), (before, body) => before.Then(body));
    }

    // The most the paths through a part need: Plain over those that write no seed, Seeded over those that do, without
    // the seed itself, which a path counts once however many seed steps it passes. Null is no such path.
    private readonly record struct Paths(long? Plain, long? Seeded)
    {
        public static Paths None => new(null, null);

        public Paths Then(Paths next) => new(
            Plain + next.Plain,
            Max(Max(Seeded + next.Plain, Plain + next.Seeded), Seeded + next.Seeded));

        public Paths Or(Paths other) => new(Max(Plain, other.Plain), Max(Seeded, other.Seeded));

        private static long? Max(long? first, long? second) =>
            first is { } one && second is { } two ? Math.Max(one, two) : first ?? second;
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// What the validator knows at a point of a sequence about every path that reaches it: the phases a path may be in there,
// what may have happened on some path, and what must have happened on every path. Joining the states of two paths
// keeps what either may have done and what both must have done.
internal readonly record struct PathState(PathPhases Phases, Happened May, Happened Must)
{
    // A run starts in Windows PE with nothing done.
    public static PathState Start { get; } = new(PathPhases.WindowsPE, Happened.None, Happened.None);

    // No path at all, which a join leaves as the other state.
    public static PathState Never { get; } = new(PathPhases.None, Happened.None, Happened.All);

    // The phases a node without a phase of its own runs in here.
    public PhaseSet Current =>
        ((Phases & (PathPhases.WindowsPE | PathPhases.WindowsPEAfterWindows)) != 0 ? PhaseSet.WindowsPE : PhaseSet.None)
        | ((Phases & PathPhases.Windows) != 0 ? PhaseSet.Windows : PhaseSet.None);

    public PathState Join(PathState other) => new(Phases | other.Phases, May | other.May, Must & other.Must);

    public PathState With(Happened happened) => new(Phases, May | happened, Must | happened);

    public bool MayHave(Happened happened) => (May & happened) != 0;

    public bool MustHave(Happened happened) => (Must & happened) == happened;

    // The state once a node that needs a phase has it. Windows PE after Windows is kept apart, so that only the paths
    // that really went back are refused.
    public PathState Enter(SequencePhase? phase) => phase switch
    {
        SequencePhase.Windows => this with { Phases = Phases == PathPhases.None ? PathPhases.None : PathPhases.Windows },
        SequencePhase.WindowsPE => this with
        {
            Phases = (Phases & PathPhases.WindowsPE)
                | ((Phases & (PathPhases.Windows | PathPhases.WindowsPEAfterWindows)) != 0 ? PathPhases.WindowsPEAfterWindows : PathPhases.None),
        },
        _ => this,
    };
}

[Flags]
internal enum PathPhases
{
    None = 0,
    WindowsPE = 1,
    Windows = 2,
    WindowsPEAfterWindows = 4,
}

[Flags]
internal enum PhaseSet
{
    None = 0,
    WindowsPE = 1,
    Windows = 2,
}

// What a sequence does at most once in a run, and what later steps rely on. ImageEveryTime is an image applied by a
// step without conditions of its own, which Windows needs.
[Flags]
internal enum Happened
{
    None = 0,
    Partitioned = 1,
    ImageApplied = 2,
    ImageEveryTime = 4,
    UnattendWritten = 8,
    DomainJoined = 16,
    RawImageWritten = 32,
    SeedWritten = 64,
    All = 127,
}

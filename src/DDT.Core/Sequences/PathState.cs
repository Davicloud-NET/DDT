// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// What the validator knows of every path that reaches a node: the phases a path may be in, what some path may have done
// and what every path must have done. A join keeps what either may have done and what both must have done.
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

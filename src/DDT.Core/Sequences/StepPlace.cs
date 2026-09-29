// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// What SequencePaths knows at a step. That's the paths that reach it, the phase it asks for and the phases it runs in.
// A rule that needs something done first asks whether every path did it. A rule that allows something only once asks
// whether any path did.
internal readonly record struct StepPlace(PathState Before, SequencePhase? Required, PhaseSet RunsIn, bool InRepeat)
{
    public bool InWindowsPE => (RunsIn & PhaseSet.WindowsPE) != 0;

    public bool Partitioned => Before.MustHave(Happened.Partitioned);

    // Inside a repeat, a step that runs only once is already refused by SequenceOnceInRepeat. So this doesn't also
    // report that a later iteration does it again.
    public bool Again(Happened happened) => !InRepeat && Before.MayHave(happened);
}

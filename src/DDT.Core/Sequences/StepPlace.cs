// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// Where SequencePaths meets a step: the paths that reach it, the phase it asks for and the phases it runs in. A rule
// that needs something done first asks whether every path did it; a rule that allows something once, whether any did.
internal readonly record struct StepPlace(PathState Before, SequencePhase? Required, PhaseSet RunsIn, bool InRepeat)
{
    public bool InWindowsPE => (RunsIn & PhaseSet.WindowsPE) != 0;

    public bool Partitioned => Before.MustHave(Happened.Partitioned);

    // Inside a repeat, what a run does once is refused as such; that a later time round does it again goes unsaid.
    public bool Again(Happened happened) => !InRepeat && Before.MayHave(happened);
}

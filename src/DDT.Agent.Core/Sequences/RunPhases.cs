// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// A sequence's phases by role: the boot environment it starts in, and the installed system it goes on in.
internal static class RunPhases
{
    // Every sequence starts in WinPE, whatever its steps are.
    public static SequencePhase BootOf(SequenceDefinition sequence) => SequencePhase.WindowsPE;

    public static bool IsInstalled(SequencePhase phase) => phase == SequencePhase.Windows;
}

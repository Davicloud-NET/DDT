// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// After RebootRequired or PhaseChangeRequired, the host restarts the machine or hands the run over to Windows. Then it
// calls the engine again with the saved state.
public enum SequenceOutcome
{
    Completed,
    Failed,
    RebootRequired,
    PhaseChangeRequired,
    Stopped,
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// RebootRequired and PhaseChangeRequired leave the host to restart the machine or hand the run over to Windows, and
// to call the engine again with the saved state.
public enum SequenceOutcome
{
    Completed,
    Failed,
    RebootRequired,
    PhaseChangeRequired,
    Stopped,
}

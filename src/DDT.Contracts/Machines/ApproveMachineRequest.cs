// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Contracts.Machines;

public sealed record ApproveMachineRequest(
    // The sequence the page showed as chosen by an assignment rule for this machine. The approval then runs it, and
    // is refused if the rules now choose something else. Without it, an approval runs nothing.
    Guid? ExpectedSequenceId,
    // This and Answers work as for an assignment, and apply to the run of that sequence.
    bool AllowSecureBootMismatch = false,
    IReadOnlyList<InputAnswer>? Answers = null);

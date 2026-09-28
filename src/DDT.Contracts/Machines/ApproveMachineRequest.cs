// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Contracts.Machines;

public sealed record ApproveMachineRequest(
    // The sequence the page showed an assignment rule choosing for the machine. The approval then runs it, and is
    // refused when the rules choose otherwise by now. Without it an approval runs nothing.
    Guid? ExpectedSequenceId,
    // This and Answers are as for an assignment, for the run of that sequence.
    bool AllowSecureBootMismatch = false,
    IReadOnlyList<InputAnswer>? Answers = null);

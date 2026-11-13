// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // The Windows Update step.

    public static readonly MessageTemplate SequenceUpdatesRestarts = Define(
        "sequence.updatesRestarts",
        "Allow 1 to {max} restarts.");

    public static readonly MessageTemplate SequenceUpdatesTooManyKbs = Define(
        "sequence.updatesTooManyKbs",
        "A step leaves out at most {max} updates.");

    public static readonly MessageTemplate SequenceUpdatesKb = Define(
        "sequence.updatesKb",
        "Enter a KB number, such as KB5031234.");

    public static readonly MessageTemplate SequenceUpdatesKbRepeated = Define(
        "sequence.updatesKbRepeated",
        "This update is listed twice.");
}

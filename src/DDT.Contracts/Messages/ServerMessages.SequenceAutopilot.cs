// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // The Register with Autopilot step.

    public static readonly MessageTemplate SequenceAutopilotGroupTag = Define(
        "sequence.autopilotGroupTag",
        "A group tag has at most {max} characters, without commas or quotation marks.");

    public static readonly MessageTemplate SequenceAutopilotWaitMinutes = Define(
        "sequence.autopilotWaitMinutes",
        "Wait 1 to {max} minutes.");
}

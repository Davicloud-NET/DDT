// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Step kinds that hand records to the server, as SequenceStep's discriminators name them.
public static class StepKindNames
{
    public const string EnableBitLocker = "enableBitLocker";

    public const string RegisterAutopilot = "registerAutopilot";
}

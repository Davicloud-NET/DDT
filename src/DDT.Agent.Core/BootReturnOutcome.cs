// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// How a start of the boot environment goes on once it has looked for a run that was handed over. With an exit code
// the agent ends with it. Without one it registers as after any start, with what the look left it.
public sealed record BootReturnOutcome(int? ExitCode)
{
    public static BootReturnOutcome GoesOn { get; } = new((int?)null);

    // The resume token of the last registration the look made, or the one the firmware carried. The loop's own goes on
    // from it, so the machine is not started over a second time.
    public string? ResumeToken { get; init; }
}

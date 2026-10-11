// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// How a start of the boot environment goes on once it has looked for a run that was handed over. With an exit code
// the agent ends with it. Without one it registers as after any start, with what the look left it.
public sealed record BootReturnOutcome(int? ExitCode)
{
    public static BootReturnOutcome GoesOn { get; } = new((int?)null);

    // Someone at the machine chose to start over, which the registration tells the server.
    public bool StartsOver { get; init; }

    // The resume token of the registration the look made with the run's token. The loop's own goes on from it.
    public string? ResumeToken { get; init; }
}

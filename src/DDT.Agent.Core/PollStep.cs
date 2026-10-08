// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// What polling does next: wait for the next poll, poll again at once, or end. A null ExitCode means register again.
internal readonly record struct PollStep(bool Ends, int? ExitCode, bool AtOnce)
{
    public static PollStep Wait => new(false, null, false);

    public static PollStep Now => new(false, null, true);

    public static PollStep End(int? exitCode) => new(true, exitCode, false);
}

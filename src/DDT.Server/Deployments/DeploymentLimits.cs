// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

public static class DeploymentLimits
{
    // The column's length. PostgreSQL refuses a longer value, and the agent would resend its report forever.
    public const int MaxErrorLength = 1024;

    // SequenceValidator allows 100 characters, and a draft saved with more is never assigned.
    public const int MaxStepNameLength = 128;

    public const int MaxRequestBytes = 16 * 1024;

    // A report names every step that has left Pending, and several failed steps can each carry a long error.
    public const int MaxReportBytes = 64 * 1024;

    // Two last-seen resolutions and a poll. A Pending machine seen longer ago may not be the one at the prompt
    // any more, and whoever still holds its tokens must not be authorized by an assignment.
    public static readonly TimeSpan WaitingAtPrompt = TimeSpan.FromSeconds(90);
}

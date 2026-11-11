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

    // The column's length. A pause message the agent reports is cut to fit, like an error, instead of being refused.
    public const int MaxPauseMessageLength = 1024;

    public const int MaxRequestBytes = 16 * 1024;

    // A report names every step that has left Pending, and several failed steps can each carry a long error.
    public const int MaxReportBytes = 64 * 1024;

    // Two last-seen resolutions and a poll. A Pending machine seen longer ago may not be the one at the prompt
    // any more, and whoever still holds its tokens must not be authorized by an assignment.
    public static readonly TimeSpan WaitingAtPrompt = TimeSpan.FromSeconds(90);

    // How long a restart into the other boot environment may take. After that the machine's resume token no longer
    // crosses to it, and netboot no longer starts it there.
    public static readonly TimeSpan EnvironmentSwitch = TimeSpan.FromMinutes(15);

    // How often the server restarts a machine for a run of the other boot environment. Netboot may never start it there.
    public const int RestartOrders = 2;

    // Far more starts of the boot environment after a hand-over than an agent counts before it gives the run up.
    public const int MaxBootReturns = 100;

    // Far more restarts in one visit of a step than any step's limit allows.
    public const int MaxStepRestarts = 100;

    // How long an agent told to restart counts as about to. It restarts at once, so one that still polls then did not.
    public static readonly TimeSpan RestartOrderOpen = TimeSpan.FromSeconds(30);
}

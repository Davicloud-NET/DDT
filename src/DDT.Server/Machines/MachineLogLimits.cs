// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

public static class MachineLogLimits
{
    public const int MaxLinesPerBatch = 200;
    public const int MaxMessageLength = 4000;
    public const int MaxRequestBytes = 1024 * 1024;
    public const int MaxLinesPerRead = 1000;
    public const int DefaultLinesPerRead = 500;

    // A sequence that runs scripts and installs drivers logs far more than an image deployment did.
    public const int MaxStoredLinesPerMachine = 50_000;

    public const int MaxRegistrationBytes = 64 * 1024;
    public const int MaxSignInBytes = 4 * 1024;
    public const int MaxSignInFieldLength = 512;

    // A machine polls every ten seconds, so this leaves room for its log flushes and retries while
    // bounding what one stolen token can make the server write.
    public const int MaxAgentRequestsPerMinute = 60;

    // A poll only records that the machine was seen, and a write plus a push per machine every ten
    // seconds buys nothing an operator can see.
    public static readonly TimeSpan LastSeenResolution = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan WaitingMachineLifetime = TimeSpan.FromDays(1);

    // Below this, the difference between the agent's clock and the server's is the network's delay.
    public static readonly TimeSpan SkewTolerance = TimeSpan.FromSeconds(2);
}

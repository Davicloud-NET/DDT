// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// WorkDirectory keeps the run's files until Partition gives it a directory on the disk; in a dry run it is under the
// dry run's root. SystemDirectory is where Windows PE keeps DISM and PowerShell.
public sealed record SequenceRunnerOptions(TimeSpan HeartbeatInterval, string WorkDirectory, string SystemDirectory, bool DryRun);

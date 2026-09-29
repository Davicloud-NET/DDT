// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// WorkDirectory keeps the run's files until Partition gives the run a directory on disk. In a dry run it's under the
// dry run's root. SystemDirectory is where WinPE keeps DISM and PowerShell.
public sealed record SequenceRunnerOptions(TimeSpan HeartbeatInterval, string WorkDirectory, string SystemDirectory, bool DryRun);

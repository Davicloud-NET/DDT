// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The machines section. ZeroTouchNetworks are networks such as 10.20.0.0/16.
public sealed record MachineSettings(bool RequireWebApproval, int MaxWaitingPerAddress, int MaxWaiting, IReadOnlyList<string> ZeroTouchNetworks);

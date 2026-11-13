// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// What asks an assigned run to start: its agent's report, or answers the machine just gave (MachineAnswered).
public sealed record RunStartCause(string? Address, DateTimeOffset Now, bool MachineAnswered = false);

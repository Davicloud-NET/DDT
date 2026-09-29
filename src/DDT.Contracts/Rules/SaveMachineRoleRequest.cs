// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Values;

namespace DDT.Contracts.Rules;

// Revision is the one the page last read. A new role doesn't have one, so the server ignores it.
public sealed record SaveMachineRoleRequest(long Revision, string Name, string? Description, IReadOnlyList<NamedValue> Values);

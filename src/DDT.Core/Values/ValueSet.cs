// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Values;

namespace DDT.Core.Values;

// A rule or a machine role and the values it sets, in its order.
public sealed record ValueSet(Guid Id, string Name, IReadOnlyList<NamedValue> Values);

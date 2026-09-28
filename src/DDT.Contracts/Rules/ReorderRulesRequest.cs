// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Rules;

// Every rule's id in the new order, top first. Refused unless it names exactly the rules there are, so an order made
// before someone added or removed a rule never applies.
public sealed record ReorderRulesRequest(IReadOnlyList<Guid> RuleIds);

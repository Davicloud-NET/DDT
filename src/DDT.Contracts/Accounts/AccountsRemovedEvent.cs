// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Accounts;

// Sent to every connection when accounts are deleted, so the Accounts page drops them without reading its list again.
public sealed record AccountsRemovedEvent(IReadOnlyList<Guid> AccountIds);

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

// Sent to administrators when accounts are deleted, so a page drops them from its list without reading it again.
public sealed record UsersRemovedEvent(IReadOnlyList<Guid> UserIds);

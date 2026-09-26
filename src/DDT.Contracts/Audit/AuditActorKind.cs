// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Audit;

// Who did what an audit entry records. A user signed in at a machine is a User, and a user's API token is a Token.
// System is DDT itself, such as the sweeper that ends a run whose machine went silent.
public enum AuditActorKind
{
    User,
    Machine,
    Token,
    System,
}

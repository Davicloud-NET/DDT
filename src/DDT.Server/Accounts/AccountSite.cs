// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Server.Accounts;

// A place where a step names an account. Field names the place the same way a sequence problem does. Reference is
// null for a share whose account was left out of a document from outside the server.
public sealed record AccountSite(SequenceStep Step, string Field, AccountReference? Reference, AccountPurpose Purpose, string? SharePath);

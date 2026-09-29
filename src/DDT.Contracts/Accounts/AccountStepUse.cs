// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Accounts;

// Field is the step field that names the account, written like a sequence problem's field, such as runAs, account
// or shares[0].account.
public sealed record AccountStepUse(Guid StepId, string StepName, string Field);

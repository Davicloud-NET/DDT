// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Accounts;

// Field is where the step names the account, as a sequence problem names it: runAs, account or shares[0].account.
public sealed record AccountStepUse(Guid StepId, string StepName, string Field);

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Accounts;

// Steps are the steps of the sequence that name the account, in the order of its tree.
public sealed record AccountUse(Guid SequenceId, string SequenceName, IReadOnlyList<AccountStepUse>? Steps = null);

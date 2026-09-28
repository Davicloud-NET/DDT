// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Settings;

namespace DDT.Contracts.Accounts;

// An account steps use, stored with its password encrypted; the password never leaves the server except to the step
// that uses it, while it runs. It is bound to its destinations: Domain, the domain a join with it may join, Hosts, the
// share hosts it may connect to, and RunAs, whether a script may run as it. UsedBy lists the sequences that name it.
public sealed record AccountView(
    Guid Id,
    string Name,
    string UserName,
    string? Domain,
    IReadOnlyList<string> Hosts,
    bool RunAs,
    SecretState Password,
    IReadOnlyList<AccountUse> UsedBy,
    long Revision,
    DateTimeOffset UpdatedUtc,
    string? UpdatedBy);

// Steps are the steps of the sequence that name the account, in the order of its tree.
public sealed record AccountUse(Guid SequenceId, string SequenceName, IReadOnlyList<AccountStepUse>? Steps = null);

// Field is where the step names the account, as a sequence problem names it: runAs, account or shares[0].account.
public sealed record AccountStepUse(Guid StepId, string StepName, string Field);

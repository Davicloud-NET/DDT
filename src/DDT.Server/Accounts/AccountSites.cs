// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Server.Accounts;

public enum AccountPurpose
{
    // A script runs as the account, in Windows.
    RunAs,

    // A Join the domain step joins with it.
    Join,

    // DDT connects a share with it while the step runs.
    Share,
}

// Where a step names an account. Field is the place in the step, as a sequence problem names it; Reference is null for
// a share whose account a document from outside left out. SharePath is the share's path as written, for a share.
public sealed record AccountSite(SequenceStep Step, string Field, AccountReference? Reference, AccountPurpose Purpose, string? SharePath);

public static class AccountSites
{
    // Every place the sequence names an account, through the whole of its tree, in the order of the tree.
    public static IReadOnlyList<AccountSite> Of(SequenceDefinition definition)
    {
        List<AccountSite> sites = [];

        foreach (SequenceStep step in SequenceTree.Nodes(definition))
        {
            if (step is RunScriptStep { RunAs: { } runAs })
            {
                sites.Add(new AccountSite(step, "runAs", runAs, AccountPurpose.RunAs, null));
            }

            if (step is JoinDomainStep { Account: { } account })
            {
                sites.Add(new AccountSite(step, "account", account, AccountPurpose.Join, null));
            }

            IReadOnlyList<ShareConnection?> shares = step.Shares ?? [];

            for (int index = 0; index < shares.Count; index++)
            {
                if (shares[index] is { } share)
                {
                    sites.Add(new AccountSite(step, $"shares[{index}].account", share.Account, AccountPurpose.Share, share.Path));
                }
            }
        }

        return sites;
    }
}

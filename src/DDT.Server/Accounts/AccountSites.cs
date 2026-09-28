// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Server.Accounts;

public static class AccountSites
{
    // Every place the sequence names an account, nested steps included, in tree order.
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

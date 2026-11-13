// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Accounts;
using DDT.Server.Applications;
using DDT.Server.Live;

namespace DDT.Server.Sequences;

// Pushes the accounts, applications and bundles whose "used by" a sequence save changed. A null definition means the
// sequence doesn't exist yet, or no longer does.
internal sealed class SequenceUseUpdates(AccountViews accounts, ApplicationViews applications, LiveNotifier live)
{
    public async Task PushAsync(string? before, string? beforeName, string? after, string? afterName, CancellationToken cancellationToken)
    {
        await accounts.PushUsesAsync(live, AccountViews.UsesChanged(before, beforeName, after, afterName), cancellationToken).ConfigureAwait(false);
        await applications.PushAsync(live, ApplicationUses.Changed(before, beforeName, after, afterName), cancellationToken).ConfigureAwait(false);
    }
}

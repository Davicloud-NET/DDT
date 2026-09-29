// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Accounts;
using DDT.Contracts.Settings;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Accounts;

// Accounts as the Accounts page and the sequence checks see them. They never hold the password, only whether it's set
// and this server can read it. Uses are worked out from the stored sequences every time. A small library allows that.
public sealed class AccountViews(DdtDbContext database, AccountProtector protector)
{
    public async Task<IReadOnlyList<AccountView>> ListAsync(CancellationToken cancellationToken)
    {
        List<Account> accounts = await database.Accounts.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, List<AccountUse>> uses = await UsesAsync(cancellationToken).ConfigureAwait(false);

        // SQLite cannot order by every column type, and the list is small, so it is ordered here.
        return [.. accounts.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Id).Select(a => View(a, uses))];
    }

    public async Task<AccountView> ViewAsync(Account account, CancellationToken cancellationToken) =>
        View(account, await UsesAsync(cancellationToken).ConfigureAwait(false));

    public AccountView View(Account account, IReadOnlyDictionary<Guid, List<AccountUse>> uses)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(uses);

        return new AccountView(
            account.Id,
            account.Name,
            account.UserName,
            account.Domain,
            AccountRules.ReadHosts(account.Hosts),
            account.RunAs,
            Password(account),
            uses.GetValueOrDefault(account.Id) ?? [],
            account.Revision,
            account.UpdatedUtc,
            account.UpdatedByName);
    }

    // Works like a secret setting. A password that no longer decrypts counts as not set, and the state says so.
    public SecretState Password(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        bool stored = account.ProtectedPassword is not null;
        bool readable = stored && Readable(account);

        return new SecretState(readable, stored && !readable, stored ? account.PasswordUpdatedUtc : null);
    }

    public bool Readable(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return account.ProtectedPassword is { } stored && protector.Unprotect(account.Id, stored) is not null;
    }

    // The facts the sequence checks need, by account ID.
    public async Task<IReadOnlyDictionary<Guid, AccountFacts>> FactsAsync(CancellationToken cancellationToken)
    {
        List<Account> accounts = await database.Accounts.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        return accounts.ToDictionary(
            account => account.Id,
            account => new AccountFacts(
                account.Name,
                account.Domain,
                AccountRules.ReadHosts(account.Hosts),
                account.RunAs,
                account.ProtectedPassword is not null,
                Readable(account)));
    }

    // For each stored account ID, the sequences and steps that use it. This walks the whole tree of every stored
    // sequence. The sequences are in the same order as on the Sequences page.
    public async Task<Dictionary<Guid, List<AccountUse>>> UsesAsync(CancellationToken cancellationToken)
    {
        List<TaskSequence> sequences = await database.TaskSequences.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, List<AccountUse>> uses = [];

        foreach (TaskSequence sequence in sequences.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id))
        {
            IEnumerable<IGrouping<Guid, AccountSite>> named = AccountSites.Of(SequenceDocuments.Read(sequence.Definition))
                .Where(site => site.Reference?.AccountId is not null)
                .GroupBy(site => site.Reference!.AccountId!.Value);

            foreach (IGrouping<Guid, AccountSite> account in named)
            {
                if (!uses.TryGetValue(account.Key, out List<AccountUse>? list))
                {
                    uses[account.Key] = list = [];
                }

                list.Add(new AccountUse(
                    sequence.Id,
                    sequence.Name,
                    [.. account.Select(site => new AccountStepUse(site.Step.Id, site.Step.Name ?? "", site.Field))]));
            }
        }

        return uses;
    }

    // The stored accounts whose uses changed with a sequence save. A null definition means the sequence doesn't exist
    // yet, or no longer does. An autosave that doesn't touch accounts pushes nothing.
    public static IReadOnlyList<Guid> UsesChanged(string? before, string? beforeName, string? after, string? afterName)
    {
        Dictionary<Guid, string> was = Uses(before, beforeName);
        Dictionary<Guid, string> now = Uses(after, afterName);

        return [.. was.Keys.Union(now.Keys).Where(id => was.GetValueOrDefault(id) != now.GetValueOrDefault(id))];
    }

    // For each account the sequence uses, one text that says how it's used, so two versions can be compared.
    private static Dictionary<Guid, string> Uses(string? definition, string? name) =>
        definition is null
            ? []
            : AccountSites.Of(SequenceDocuments.Read(definition))
                .Where(site => site.Reference?.AccountId is not null)
                .GroupBy(site => site.Reference!.AccountId!.Value)
                .ToDictionary(
                    account => account.Key,
                    account => string.Join('\n', [name, .. account.Select(site => $"{site.Step.Id:D} {site.Field} {site.Step.Name}")]));

    // A sequence that starts or stops using an account, or is renamed, changes the uses the Accounts page shows.
    public async Task PushUsesAsync(LiveNotifier live, IEnumerable<Guid> accountIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(accountIds);

        HashSet<Guid> ids = [.. accountIds];

        if (ids.Count == 0)
        {
            return;
        }

        List<Account> accounts = await database.Accounts
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (accounts.Count == 0)
        {
            return;
        }

        Dictionary<Guid, List<AccountUse>> uses = await UsesAsync(cancellationToken).ConfigureAwait(false);

        foreach (Account account in accounts)
        {
            live.AccountChanged(View(account, uses));
        }
    }
}

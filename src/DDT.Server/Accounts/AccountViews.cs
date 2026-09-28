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

// Accounts as the Accounts page and the sequence checks see them: never the password, only whether it is set and this
// server can read it. Uses are worked out from the stored sequences each time, which a small library allows.
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

    // As for a setting's secret: one that no longer decrypts is not set, and says so.
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

    // For the checks of sequences, by id.
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

    // The sequences that name each stored account, by the account's id, with the steps that name it, walked through the
    // whole tree of every stored sequence. Sequences in the order the Sequences page lists them.
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

    // The stored accounts whose uses a save of a sequence changed; a null definition is a sequence that does not exist yet
    // or any more. An autosave that changes nothing about accounts pushes nothing.
    public static IReadOnlyList<Guid> UsesChanged(string? before, string? beforeName, string? after, string? afterName)
    {
        Dictionary<Guid, string> was = Uses(before, beforeName);
        Dictionary<Guid, string> now = Uses(after, afterName);

        return [.. was.Keys.Union(now.Keys).Where(id => was.GetValueOrDefault(id) != now.GetValueOrDefault(id))];
    }

    // For each account the sequence names, how it names it, as one text to compare.
    private static Dictionary<Guid, string> Uses(string? definition, string? name) =>
        definition is null
            ? []
            : AccountSites.Of(SequenceDocuments.Read(definition))
                .Where(site => site.Reference?.AccountId is not null)
                .GroupBy(site => site.Reference!.AccountId!.Value)
                .ToDictionary(
                    account => account.Key,
                    account => string.Join('\n', [name, .. account.Select(site => $"{site.Step.Id:D} {site.Field} {site.Step.Name}")]));

    // A sequence that starts or stops naming an account, or is renamed, changes what its page says it is used by.
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

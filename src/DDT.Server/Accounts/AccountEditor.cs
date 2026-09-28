// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Accounts;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Accounts;

// Saves the accounts steps use, each change with its audit row. A stored password goes only to the user name, domain and
// servers it was entered for: keeping it while any of them changes is refused and audited.
internal sealed class AccountEditor(
    DdtDbContext database,
    AccountProtector protector,
    AccountViews views,
    LiveNotifier live,
    TimeProvider timeProvider)
{
    public async Task<EditOutcome<AccountView>> CreateAsync(SaveAccountRequest request, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        (AccountFields? fields, FieldProblems? problems) = await FieldsAsync(request, null, cancellationToken).ConfigureAwait(false);

        if (fields is null)
        {
            return EditOutcome<AccountView>.Invalid(problems ?? new FieldProblems());
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string? password = NewPassword(request.Password);
        Account account = new()
        {
            Id = Guid.CreateVersion7(now),
            Name = fields.Name,
            NormalizedName = Normalize(fields.Name),
            UserName = fields.UserName,
            Domain = fields.Domain,
            Hosts = AccountRules.WriteHosts(fields.Hosts),
            RunAs = fields.RunAs,
            Revision = 1,
            CreatedUtc = now,
            UpdatedUtc = now,
            UpdatedByUserId = actor.UserId,
            UpdatedByName = actor.Name,
        };

        if (password is not null)
        {
            account.ProtectedPassword = protector.Protect(account.Id, password);
            account.PasswordUpdatedUtc = now;
        }

        database.Accounts.Add(account);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.AccountCreated,
            account.Id.ToString("D"),
            actor,
            now,
            $"Created the account {account.Name}: {AccountChanges.Created(fields, password is not null)}."));

        return await SavedAsync(account, cancellationToken).ConfigureAwait(false);
    }

    // A save names the revision it was made on, and one over a newer revision gets the account as it is now.
    public async Task<EditOutcome<AccountView>> SaveAsync(Guid id, SaveAccountRequest request, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        Account? account = await database.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);

        if (account is null)
        {
            return EditOutcome<AccountView>.NotFound;
        }

        if (request.Revision != account.Revision)
        {
            return EditOutcome<AccountView>.Newer(await views.ViewAsync(account, cancellationToken).ConfigureAwait(false));
        }

        (AccountFields? fields, FieldProblems? problems) = await FieldsAsync(request, id, cancellationToken).ConfigureAwait(false);

        if (fields is null)
        {
            return EditOutcome<AccountView>.Invalid(problems ?? new FieldProblems());
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string? password = NewPassword(request.Password);
        bool clear = password is null && request.Password?.Action is SecretAction.Set or SecretAction.Clear;

        if (password is null && !clear && account.ProtectedPassword is not null
            && await KeptPasswordRefusalAsync(account, fields, actor, now, cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return EditOutcome<AccountView>.Invalid(refused);
        }

        List<string> changes = AccountChanges.Of(account, fields, password is not null, clear && account.ProtectedPassword is not null);

        // A save of what is stored already changes nothing and records nothing.
        if (changes.Count == 0)
        {
            return EditOutcome<AccountView>.Done(await views.ViewAsync(account, cancellationToken).ConfigureAwait(false));
        }

        Apply(account, fields, request.Password, actor, now);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.AccountChanged,
            account.Id.ToString("D"),
            actor,
            now,
            $"Changed the account {account.Name}: {string.Join(", ", changes)}."));

        return await SavedAsync(account, cancellationToken).ConfigureAwait(false);
    }

    // Refused while a sequence names it: a run of that sequence would fail at the step, after the disk was erased. False
    // for an account that is gone.
    public async Task<(bool Found, ServerMessage? Refusal)> DeleteAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        Account? account = await database.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);

        if (account is null)
        {
            return (false, null);
        }

        if ((await views.UsesAsync(cancellationToken).ConfigureAwait(false)).GetValueOrDefault(id) is { Count: > 0 } uses)
        {
            return (true, ServerMessages.StepAccountInUse.With("count", uses.Count, "sequences", string.Join(", ", uses.Select(use => use.SequenceName))));
        }

        database.Accounts.Remove(account);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.AccountDeleted,
            account.Id.ToString("D"),
            actor,
            timeProvider.GetUtcNow(),
            $"Deleted the account {account.Name} ({account.UserName})."));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (true, ServerMessages.StepAccountChangedMeanwhile.With());
        }

        live.AccountsRemoved([id]);

        return (true, null);
    }

    // A kept password that no longer decrypts, or would reach a new destination. The second is recorded alone: nothing
    // else of the refused save is stored.
    private async Task<FieldProblems?> KeptPasswordRefusalAsync(
        Account account,
        AccountFields fields,
        Actor actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        FieldProblems problems = new();

        if (!views.Readable(account))
        {
            problems.Add("password", ServerMessages.StepAccountPasswordUnreadable.With());

            return problems;
        }

        if (AccountChanges.NewDestination(account, fields) is not { Count: > 0 } moved)
        {
            return null;
        }

        database.ChangeTracker.Clear();
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.AccountRefused,
            account.Id.ToString("D"),
            actor,
            now,
            $"Refused a save of the account {account.Name} that kept its stored password for a new destination: {string.Join(", ", moved)}."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        problems.Add("password", ServerMessages.StepAccountPasswordForNewDestination.With());

        return problems;
    }

    // A set without a value clears the password, as for a setting's secret.
    private void Apply(Account account, AccountFields fields, SecretUpdate? update, Actor actor, DateTimeOffset now)
    {
        string? password = NewPassword(update);
        bool clear = password is null && update?.Action is SecretAction.Set or SecretAction.Clear;

        account.Name = fields.Name;
        account.NormalizedName = Normalize(fields.Name);
        account.UserName = fields.UserName;
        account.Domain = fields.Domain;
        account.Hosts = AccountRules.WriteHosts(fields.Hosts);
        account.RunAs = fields.RunAs;

        if (password is not null)
        {
            account.ProtectedPassword = protector.Protect(account.Id, password);
            account.PasswordUpdatedUtc = now;
        }
        else if (clear && account.ProtectedPassword is not null)
        {
            account.ProtectedPassword = null;
            account.PasswordUpdatedUtc = now;
        }

        account.Revision++;
        account.UpdatedUtc = now;
        account.UpdatedByUserId = actor.UserId;
        account.UpdatedByName = actor.Name;
    }

    // Saved and pushed; the name another save took at the same moment, or the account as it is now after a newer save.
    private async Task<EditOutcome<AccountView>> SavedAsync(Account account, CancellationToken cancellationToken)
    {
        try
        {
            if (await CommitAsync(account, cancellationToken).ConfigureAwait(false) is { } taken)
            {
                return EditOutcome<AccountView>.Invalid(taken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();
            Account? current = await database.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == account.Id, cancellationToken).ConfigureAwait(false);

            return current is null
                ? EditOutcome<AccountView>.NotFound
                : EditOutcome<AccountView>.Newer(await views.ViewAsync(current, cancellationToken).ConfigureAwait(false));
        }

        AccountView view = await views.ViewAsync(account, cancellationToken).ConfigureAwait(false);
        live.AccountChanged(view);

        return EditOutcome<AccountView>.Done(view);
    }

    private async Task<(AccountFields? Fields, FieldProblems? Problems)> FieldsAsync(SaveAccountRequest request, Guid? id, CancellationToken cancellationToken)
    {
        FieldProblems problems = new();
        string name = request.Name?.Trim() ?? "";

        if (name.Length is 0 or > AccountLimits.MaxNameLength || name.Any(char.IsControl))
        {
            problems.Add("name", ServerMessages.NameLength.With("max", AccountLimits.MaxNameLength));
        }
        else if (await NameTakenAsync(id, name, cancellationToken).ConfigureAwait(false))
        {
            problems.Add("name", ServerMessages.StepAccountNameTaken.With("name", name));
        }

        if (AccountRules.UserNameProblem(request.UserName) is { } userName)
        {
            problems.Add("userName", userName);
        }

        string? domain = AccountRules.Trimmed(request.Domain);

        if (domain is not null && !AccountRules.IsDomain(domain))
        {
            problems.Add("domain", ServerMessages.StepAccountDomainInvalid.With("value", domain));
        }

        List<string> hosts = Hosts(request.Hosts ?? [], problems);

        if (NewPassword(request.Password) is { } password && AccountRules.PasswordProblem(password) is { } refused)
        {
            problems.Add("password", refused);
        }

        return problems.Count > 0
            ? (null, problems)
            : (new AccountFields(name, request.UserName?.Trim() ?? "", domain, hosts, request.RunAs), null);
    }

    // The servers the account may connect to, each once, or the problems of those that are none.
    private static List<string> Hosts(IReadOnlyList<string?> requested, FieldProblems problems)
    {
        List<string> hosts = [];

        if (requested.Count > AccountLimits.MaxHosts)
        {
            problems.Add("hosts", ServerMessages.StepAccountTooManyHosts.With("max", AccountLimits.MaxHosts));

            return hosts;
        }

        for (int index = 0; index < requested.Count; index++)
        {
            string? host = AccountRules.Trimmed(requested[index]);

            if (host is null || !AccountRules.IsHost(host))
            {
                problems.Add($"hosts[{index}]", ServerMessages.StepAccountHostInvalid.With("value", host ?? ""));
            }
            else if (AccountRules.Allows(hosts, host))
            {
                problems.Add($"hosts[{index}]", ServerMessages.StepAccountHostRepeated.With("value", host));
            }
            else
            {
                hosts.Add(host);
            }
        }

        return hosts;
    }

    // The password a save sets; null to keep or clear it. A set without a value clears, as for a setting's secret.
    private static string? NewPassword(SecretUpdate? update) =>
        update is { Action: SecretAction.Set, Value: { Length: > 0 } value } ? value : null;

    private Task<bool> NameTakenAsync(Guid? id, string name, CancellationToken cancellationToken)
    {
        string normalized = Normalize(name);

        return database.Accounts.AnyAsync(a => a.NormalizedName == normalized && a.Id != id, cancellationToken);
    }

    // The unique index settles two saves that took the same name at once; the loser is told as if it had been first.
    private async Task<FieldProblems?> CommitAsync(Account account, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch (DbUpdateException exception) when (exception is not DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();

            if (!await NameTakenAsync(account.Id, account.Name, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            FieldProblems taken = new();
            taken.Add("name", ServerMessages.StepAccountNameTaken.With("name", account.Name));

            return taken;
        }
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();
}

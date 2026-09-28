// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Accounts;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Accounts;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

// The accounts steps use, Deployment > Accounts. Everyone signed in reads them, never a password; only an administrator
// changes them, signed in on the web and with the password entered again, since an account reaches machines with
// whatever it may do in the domain. A stored password goes only to the user name, domain and servers it was entered for
// (settings.md 5.4): keeping it while any of them changes is refused and audited.
public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapGet("/{id:guid}", ReadAsync).RequireAuthorization(DdtPolicies.Viewer);
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut("/{id:guid}", SaveAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<AccountView>>> ListAsync(AccountViews views, CancellationToken cancellationToken) =>
        TypedResults.Ok(await views.ListAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Ok<AccountView>, NotFound>> ReadAsync(
        Guid id,
        DdtDbContext database,
        AccountViews views,
        CancellationToken cancellationToken)
    {
        Account? account = await database.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);

        return account is null ? TypedResults.NotFound() : TypedResults.Ok(await views.ViewAsync(account, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Results<Created<AccountView>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        SaveAccountRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        AccountProtector protector,
        AccountViews views,
        ReauthenticationTokens reauthentication,
        UserManager<DdtUser> users,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await RefusedAsync(user, context, reauthentication, users).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        (Fields? fields, ValidationProblem? problems) = await FieldsAsync(request, null, database, cancellationToken).ConfigureAwait(false);

        if (problems is not null)
        {
            return problems;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string? password = NewPassword(request.Password);
        Account account = new()
        {
            Id = Guid.CreateVersion7(now),
            Name = fields!.Name,
            NormalizedName = Normalize(fields.Name),
            UserName = fields.UserName,
            Domain = fields.Domain,
            Hosts = AccountRules.WriteHosts(fields.Hosts),
            RunAs = fields.RunAs,
            Revision = 1,
            CreatedUtc = now,
            UpdatedUtc = now,
            UpdatedByUserId = Principals.UserId(user),
            UpdatedByName = Principals.ActorName(user),
        };

        if (password is not null)
        {
            account.ProtectedPassword = protector.Protect(account.Id, password);
            account.PasswordUpdatedUtc = now;
        }

        List<string> described =
        [
            $"userName '{fields.UserName}'",
            $"domain '{fields.Domain}'",
            $"hosts {Hosts(fields.Hosts)}",
            $"runAs {OnOff(fields.RunAs)}",
            .. password is null ? Array.Empty<string>() : ["password set"],
        ];

        database.Accounts.Add(account);
        database.AuditEvents.Add(Audit(AuditActions.AccountCreated, account, user, context, now, $"Created the account {account.Name}: {string.Join(", ", described)}."));

        if (await CommitAsync(database, account, cancellationToken).ConfigureAwait(false) is { } taken)
        {
            return taken;
        }

        AccountView view = await views.ViewAsync(account, cancellationToken).ConfigureAwait(false);
        live.AccountChanged(view);

        return TypedResults.Created($"/api/accounts/{account.Id:D}", view);
    }

    private static async Task<Results<Ok<AccountView>, Conflict<AccountView>, NotFound, ValidationProblem, ProblemHttpResult>> SaveAsync(
        Guid id,
        SaveAccountRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        AccountProtector protector,
        AccountViews views,
        ReauthenticationTokens reauthentication,
        UserManager<DdtUser> users,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await RefusedAsync(user, context, reauthentication, users).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        Account? account = await database.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);

        if (account is null)
        {
            return TypedResults.NotFound();
        }

        // The page decides what to do with a newer save: take it, or save its own over it knowingly.
        if (request.Revision != account.Revision)
        {
            return TypedResults.Conflict(await views.ViewAsync(account, cancellationToken).ConfigureAwait(false));
        }

        (Fields? fields, ValidationProblem? problems) = await FieldsAsync(request, id, database, cancellationToken).ConfigureAwait(false);

        if (problems is not null)
        {
            return problems;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string? password = NewPassword(request.Password);
        bool clear = password is null && request.Password?.Action is SecretAction.Set or SecretAction.Clear;

        if (password is null && !clear && account.ProtectedPassword is not null)
        {
            if (!views.Readable(account))
            {
                return ServerProblems.Validation("password", ServerMessages.StepAccountPasswordUnreadable.With());
            }

            if (NewDestination(account, fields!) is { Count: > 0 } moved)
            {
                // Recorded alone: nothing else of the refused save is stored.
                database.ChangeTracker.Clear();
                database.AuditEvents.Add(Audit(
                    AuditActions.AccountRefused,
                    account,
                    user,
                    context,
                    now,
                    $"Refused a save of the account {account.Name} that kept its stored password for a new destination: {string.Join(", ", moved)}."));
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return ServerProblems.Validation("password", ServerMessages.StepAccountPasswordForNewDestination.With());
            }
        }

        List<string> changes = Changes(account, fields!, password is not null, clear && account.ProtectedPassword is not null);

        // A save of what is stored already changes nothing and records nothing.
        if (changes.Count == 0)
        {
            return TypedResults.Ok(await views.ViewAsync(account, cancellationToken).ConfigureAwait(false));
        }

        account.Name = fields!.Name;
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
        account.UpdatedByUserId = Principals.UserId(user);
        account.UpdatedByName = Principals.ActorName(user);

        database.AuditEvents.Add(Audit(AuditActions.AccountChanged, account, user, context, now, $"Changed the account {account.Name}: {string.Join(", ", changes)}."));

        try
        {
            if (await CommitAsync(database, account, cancellationToken).ConfigureAwait(false) is { } taken)
            {
                return taken;
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();
            Account? current = await database.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);

            return current is null
                ? TypedResults.NotFound()
                : TypedResults.Conflict(await views.ViewAsync(current, cancellationToken).ConfigureAwait(false));
        }

        AccountView view = await views.ViewAsync(account, cancellationToken).ConfigureAwait(false);
        live.AccountChanged(view);

        return TypedResults.Ok(view);
    }

    // Refused while a sequence names it: a run of that sequence would fail at the step, after the disk was erased.
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        AccountViews views,
        ReauthenticationTokens reauthentication,
        UserManager<DdtUser> users,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await RefusedAsync(user, context, reauthentication, users).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        Account? account = await database.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);

        if (account is null)
        {
            return TypedResults.NotFound();
        }

        if ((await views.UsesAsync(cancellationToken).ConfigureAwait(false)).GetValueOrDefault(id) is { Count: > 0 } uses)
        {
            return ServerProblems.Problem(
                ServerMessages.StepAccountInUse.With("count", uses.Count, "sequences", string.Join(", ", uses.Select(use => use.SequenceName))),
                StatusCodes.Status409Conflict);
        }

        database.Accounts.Remove(account);
        database.AuditEvents.Add(Audit(
            AuditActions.AccountDeleted,
            account,
            user,
            context,
            timeProvider.GetUtcNow(),
            $"Deleted the account {account.Name} ({account.UserName})."));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServerProblems.Problem(ServerMessages.StepAccountChangedMeanwhile.With(), StatusCodes.Status409Conflict);
        }

        live.AccountsRemoved([id]);

        return TypedResults.NoContent();
    }

    // Null when the request may write: a person signed in on the web who entered their password again in the last five
    // minutes. A token of a script proves nobody is there.
    private static async Task<ProblemHttpResult?> RefusedAsync(
        ClaimsPrincipal user,
        HttpContext context,
        ReauthenticationTokens reauthentication,
        UserManager<DdtUser> users)
    {
        if (Principals.ApiTokenId(user) is not null)
        {
            return ServerProblems.Problem(ServerMessages.StepAccountApiToken.With(), StatusCodes.Status403Forbidden);
        }

        if (!await reauthentication.ValidAsync(context, user, users).ConfigureAwait(false))
        {
            // Fields as the settings name what needs the proof, so a page asks for the password the same way.
            return ServerProblems.Problem(
                ServerMessages.StepAccountReauthenticate.With(),
                StatusCodes.Status403Forbidden,
                new Dictionary<string, object?> { ["fields"] = new[] { "account" } });
        }

        return null;
    }

    private sealed record Fields(string Name, string UserName, string? Domain, IReadOnlyList<string> Hosts, bool RunAs);

    private static async Task<(Fields? Fields, ValidationProblem? Problems)> FieldsAsync(
        SaveAccountRequest request,
        Guid? id,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        FieldProblems problems = new();
        string name = request.Name?.Trim() ?? "";

        if (name.Length is 0 or > AccountLimits.MaxNameLength || name.Any(char.IsControl))
        {
            problems.Add("name", ServerMessages.NameLength.With("max", AccountLimits.MaxNameLength));
        }
        else if (await NameTakenAsync(database, id, name, cancellationToken).ConfigureAwait(false))
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

        IReadOnlyList<string?> requested = request.Hosts ?? [];
        List<string> hosts = [];

        if (requested.Count > AccountLimits.MaxHosts)
        {
            problems.Add("hosts", ServerMessages.StepAccountTooManyHosts.With("max", AccountLimits.MaxHosts));
        }
        else
        {
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
        }

        if (NewPassword(request.Password) is { } password && AccountRules.PasswordProblem(password) is { } refused)
        {
            problems.Add("password", refused);
        }

        return problems.Count > 0
            ? (null, problems.ToResult())
            : (new Fields(name, request.UserName!.Trim(), domain, hosts, request.RunAs), null);
    }

    // The password a save sets; null to keep or clear it. A set without a value clears, as for a setting's secret.
    private static string? NewPassword(SecretUpdate? update) =>
        update is { Action: SecretAction.Set, Value: { Length: > 0 } value } ? value : null;

    // What a kept password would reach that it was not entered for: another user name or domain, or another server.
    // Fewer servers, or none, reach nothing new.
    private static List<string> NewDestination(Account account, Fields fields)
    {
        IReadOnlyList<string> before = AccountRules.ReadHosts(account.Hosts);

        return
        [
            .. AccountRules.Same(account.UserName, fields.UserName) ? Array.Empty<string>() : ["userName"],
            .. AccountRules.Same(account.Domain, fields.Domain) ? Array.Empty<string>() : ["domain"],
            .. fields.Hosts.All(host => AccountRules.Allows(before, host)) ? Array.Empty<string>() : ["hosts"],
        ];
    }

    // The fields a save changes, as the audit names them; the password only as set or cleared.
    private static List<string> Changes(Account account, Fields fields, bool passwordSet, bool passwordCleared)
    {
        List<string> changes = [];
        IReadOnlyList<string> before = AccountRules.ReadHosts(account.Hosts);

        if (account.Name != fields.Name)
        {
            changes.Add($"name '{account.Name}' to '{fields.Name}'");
        }

        if (account.UserName != fields.UserName)
        {
            changes.Add($"userName '{account.UserName}' to '{fields.UserName}'");
        }

        if (account.Domain != fields.Domain)
        {
            changes.Add($"domain '{account.Domain}' to '{fields.Domain}'");
        }

        string[] added = [.. fields.Hosts.Where(host => !before.Contains(host, StringComparer.Ordinal))];
        string[] removed = [.. before.Where(host => !fields.Hosts.Contains(host, StringComparer.Ordinal))];

        if (added.Length > 0)
        {
            changes.Add($"hosts added {Hosts(added)}");
        }

        if (removed.Length > 0)
        {
            changes.Add($"hosts removed {Hosts(removed)}");
        }

        if (added.Length == 0 && removed.Length == 0 && !before.SequenceEqual(fields.Hosts, StringComparer.Ordinal))
        {
            changes.Add($"hosts in the order {Hosts(fields.Hosts)}");
        }

        if (account.RunAs != fields.RunAs)
        {
            changes.Add($"runAs {OnOff(fields.RunAs)}");
        }

        if (passwordSet)
        {
            changes.Add("password set");
        }
        else if (passwordCleared)
        {
            changes.Add("password cleared");
        }

        return changes;
    }

    private static string Hosts(IReadOnlyList<string> hosts) => hosts.Count == 0 ? "none" : string.Join(" ", hosts);

    private static string OnOff(bool value) => value ? "on" : "off";

    private static Task<bool> NameTakenAsync(DdtDbContext database, Guid? id, string name, CancellationToken cancellationToken)
    {
        string normalized = Normalize(name);

        return database.Accounts.AnyAsync(a => a.NormalizedName == normalized && a.Id != id, cancellationToken);
    }

    // The unique index settles two saves that took the same name at once; the loser is told as if it had been first.
    private static async Task<ValidationProblem?> CommitAsync(DdtDbContext database, Account account, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch (DbUpdateException exception) when (exception is not DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();

            if (!await NameTakenAsync(database, account.Id, account.Name, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            return ServerProblems.Validation("name", ServerMessages.StepAccountNameTaken.With("name", account.Name));
        }
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();

    // Names and user names are whatever the page sent, which PostgreSQL may refuse with a NUL; the password never is.
    private static AuditEvent Audit(
        string action,
        Account account,
        ClaimsPrincipal user,
        HttpContext context,
        DateTimeOffset now,
        string detail) => new()
        {
            OccurredUtc = now,
            Action = action,
            ActorUserId = Principals.UserId(user),
            ActorName = Principals.ActorName(user),
            SubjectId = account.Id.ToString("D"),
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
        };
}

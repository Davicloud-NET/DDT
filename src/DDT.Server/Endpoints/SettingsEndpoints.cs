// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Ldap;
using DDT.Server.Live;
using DDT.Server.Security;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DDT.Server.Endpoints;

// The settings page. Every section needs the Administrator role, except that operators may read deployment and machines.
// A save answers with the whole section as it applies after the save, and every other browser receives the same view
// through the hub. Secrets are never sent back: only whether each is set.
public static class SettingsEndpoints
{
    // A subsystem this process rebuilds is quick, so a save waits this long for it before it answers Pending.
    private static readonly TimeSpan s_applyWait = TimeSpan.FromSeconds(5);

    public static RouteGroupBuilder MapSettingsEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Only a person can prove who they are; a token of a script cannot.
        group.MapPost("/reauthenticate", ReauthenticateAsync)
            .RequireAuthorization(DdtPolicies.Administrator)
            .RequireSession()
            .RequireRateLimiting(RateLimitPolicies.SignIn);

        group.MapGet(string.Empty, ReadOverviewAsync).RequireAuthorization(DdtPolicies.Administrator);

        MapSection(group, SettingsApi.Deployment);
        MapSection(group, SettingsApi.Machines);
        MapSection(group, SettingsApi.Ldap);
        MapSection(group, SettingsApi.Oidc);
        MapSection(group, SettingsApi.Proxies);
        MapSection(group, SettingsApi.Pxe);
        MapSection(group, SettingsApi.Logging);

        // Sends a password to the directory, so it is limited like a sign-in.
        group.MapPost("/ldap/test", TestLdapAsync).RequireAuthorization(DdtPolicies.Administrator).RequireRateLimiting(RateLimitPolicies.SignIn);
        group.MapPost("/oidc/test", TestOidcAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapGet("/pxe/interfaces", ReadPxeInterfacesAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/pxe/rescan", RescanAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static void MapSection<TValues>(RouteGroupBuilder group, SettingsSectionApi<TValues> api)
        where TValues : class
    {
        string route = "/" + api.Name;

        group.MapGet(route, (DdtSettings settings, SettingsViews views, CancellationToken cancellationToken) =>
                ReadAsync(api, settings, views, cancellationToken))
            .RequireAuthorization(api.OperatorsMayRead ? DdtPolicies.Operator : DdtPolicies.Administrator);

        group.MapPut(route, (
                SettingsSectionUpdate<TValues> update,
                HttpContext context,
                ClaimsPrincipal user,
                SettingsStore store,
                DdtSettings settings,
                SettingsViews views,
                SettingsHostStates hostStates,
                ReauthenticationTokens reauthentication,
                UserManager<DdtUser> users,
                LiveNotifier live,
                CancellationToken cancellationToken) =>
                SaveAsync(api, update, context, user, store, settings, views, hostStates, reauthentication, users, live, cancellationToken))
            .RequireAuthorization(DdtPolicies.Administrator);
    }

    private static async Task<Ok<SettingsSectionView<TValues>>> ReadAsync<TValues>(
        SettingsSectionApi<TValues> api,
        DdtSettings settings,
        SettingsViews views,
        CancellationToken cancellationToken)
        where TValues : class =>
        TypedResults.Ok(await views.ViewAsync(api, settings.Current, cancellationToken).ConfigureAwait(false));

    private static async Task<Ok<SettingsOverview>> ReadOverviewAsync(
        DdtSettings settings,
        SettingsViews views,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(new SettingsOverview(
            await views.SummariesAsync(settings.Current, cancellationToken).ConfigureAwait(false),
            ServerSettings.Describe(configuration, environment),
            settings.KeyRingReadable));

    private static async Task<IResult> SaveAsync<TValues>(
        SettingsSectionApi<TValues> api,
        SettingsSectionUpdate<TValues> update,
        HttpContext context,
        ClaimsPrincipal user,
        SettingsStore store,
        DdtSettings settings,
        SettingsViews views,
        SettingsHostStates hostStates,
        ReauthenticationTokens reauthentication,
        UserManager<DdtUser> users,
        LiveNotifier live,
        CancellationToken cancellationToken)
        where TValues : class
    {
        if (update?.Values is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["values"] = ["Send the values of the section."] });
        }

        if (update.Secrets?.Keys.FirstOrDefault(name => api.Definition.Field(name) is not { IsSecret: true }) is { } unknown)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [unknown] = [$"{api.Name} has no secret called {unknown}."] });
        }

        SettingsUpdate change = new()
        {
            Version = update.Version,
            Values = api.Document(update.Values),
            Secrets = update.Secrets ?? new Dictionary<string, SecretUpdate>(),
            Confirmed = new HashSet<string>(update.Confirm ?? [], StringComparer.Ordinal),
            Reauthenticated = await reauthentication.ValidAsync(context, user, users).ConfigureAwait(false),
            DirectoryProof = context.Request.Headers[DirectoryProofs.HeaderName],
        };

        SettingsSaveResult result = await store.SaveAsync(api.Definition, change, Actor(user, context), cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            SettingsSaveOutcome.Saved => await SavedAsync(api, update.Version, result.Snapshot ?? settings.Current, settings, views, hostStates, live, cancellationToken)
                .ConfigureAwait(false),
            SettingsSaveOutcome.Conflict => Conflict(api.Name),
            SettingsSaveOutcome.Reauthenticate => Reauthenticate(result.Fields ?? []),
            SettingsSaveOutcome.KeyRingUnreadable => KeyRingUnreadable(),
            _ => Invalid(result),
        };
    }

    // A save that changed the section waits a moment for this host to apply it, then pushes the view it answers with.
    private static async Task<IResult> SavedAsync<TValues>(
        SettingsSectionApi<TValues> api,
        long loadedVersion,
        SettingsSnapshot saved,
        DdtSettings settings,
        SettingsViews views,
        SettingsHostStates hostStates,
        LiveNotifier live,
        CancellationToken cancellationToken)
        where TValues : class
    {
        long version = saved[api.Name].Version;

        if (version == loadedVersion)
        {
            return TypedResults.Ok(await views.ViewAsync(api, settings.Current, cancellationToken).ConfigureAwait(false));
        }

        if (api.Definition.Kind == SettingsSectionKind.Restart && hostStates.Local(api.Name) is not null)
        {
            await hostStates.WaitAsync(api.Name, version, s_applyWait, cancellationToken).ConfigureAwait(false);
        }

        SettingsSectionView<TValues> view = await views.ViewAsync(api, settings.Current, cancellationToken).ConfigureAwait(false);
        live.SettingsChanged(view, api.OperatorsMayRead);

        return TypedResults.Ok(view);
    }

    private static ProblemHttpResult Conflict(string section) =>
        TypedResults.Problem(title: $"Someone saved {section} since you loaded it. Load it again.", statusCode: StatusCodes.Status409Conflict);

    // Fields names what needs the fresh proof, so the page can say what the password is for.
    private static ProblemHttpResult Reauthenticate(IReadOnlyList<string> fields) =>
        TypedResults.Problem(
            title: $"Enter your password again to change {string.Join(", ", fields)}.",
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["fields"] = fields.ToArray() });

    private static ProblemHttpResult KeyRingUnreadable() =>
        TypedResults.Problem(
            title: "This server cannot read the key ring the stored settings secrets were encrypted with, so it saves no settings. " +
                "Every DDT process on one database has to share the key ring in DDT:StorePath/keys.",
            statusCode: StatusCodes.Status409Conflict);

    // Problems keyed by field; the warnings still to confirm under confirm, and with their codes as an extension, so the
    // page can ask and send the codes back.
    private static ValidationProblem Invalid(SettingsSaveResult result)
    {
        Dictionary<string, string[]> errors = (result.Problems ?? [])
            .GroupBy(problem => problem.Field, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(problem => problem.Message).ToArray(), StringComparer.Ordinal);

        IReadOnlyList<SettingMessage> unconfirmed = result.Unconfirmed ?? [];

        if (unconfirmed.Count > 0)
        {
            errors["confirm"] = [.. unconfirmed.Select(warning => $"{warning.Code}: {warning.Message}")];
        }

        return TypedResults.ValidationProblem(
            errors,
            extensions: unconfirmed.Count > 0 ? new Dictionary<string, object?> { ["confirm"] = unconfirmed.ToArray() } : null);
    }

    // Checked like a sign-in, lockout and second factor included, but it signs nobody in.
    private static async Task<IResult> ReauthenticateAsync(
        ReauthenticateRequest request,
        ClaimsPrincipal principal,
        UserManager<DdtUser> users,
        CredentialVerifier credentials,
        ReauthenticationTokens tokens,
        CancellationToken cancellationToken)
    {
        DdtUser? user = await users.GetUserAsync(principal).ConfigureAwait(false);

        if (user?.UserName is null)
        {
            return TypedResults.Unauthorized();
        }

        if (user.Source == AccountSource.External || (user.Source == AccountSource.Local && !await users.HasPasswordAsync(user).ConfigureAwait(false)))
        {
            return TypedResults.Problem(
                title: "This account signs in without a password DDT can check, so it cannot change these settings. Use an account " +
                    "with a local or directory password.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        (SignInResult result, DdtUser? verified) = await credentials
            .VerifyAsync(user.UserName, request?.Password ?? string.Empty, request?.Code, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsLockedOut)
        {
            return TypedResults.Problem(title: "The account is locked out. Try again later.", statusCode: StatusCodes.Status403Forbidden);
        }

        if (result.RequiresTwoFactor)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["Enter the code of your authenticator as well."] });
        }

        if (!result.Succeeded || verified?.Id != user.Id)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["The password or the code is not right."] });
        }

        return TypedResults.Ok(tokens.Issue(verified));
    }

    // The values as the form holds them, not yet saved, with the fields configuration locks as configuration sets them.
    // A stored bind password goes only to the server it was entered for. The user part goes through the lockout of a
    // directory sign-in, and signs nobody in.
    private static async Task<IResult> TestLdapAsync(
        LdapTestRequest request,
        ClaimsPrincipal principal,
        DdtSettings settings,
        ILdapTester tester,
        UserManager<DdtUser> users,
        DirectoryProofs proofs,
        CancellationToken cancellationToken)
    {
        if (request?.Values is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["values"] = ["Send the values to test."] });
        }

        SettingsSectionState state = settings.Current[SettingsSectionNames.Ldap];
        LdapSettingsSection definition = SettingsDefinitions.Ldap;
        JsonObject values = SettingsApi.Ldap.Document(request.Values);

        foreach (SettingLockState locked in state.Locks.Where(locked => !locked.Field.IsSecret))
        {
            SettingsJson.Set(values, locked.Field, SettingsJson.Get(state.Values, locked.Field));
        }

        SettingField bindPassword = definition.Field("bindPassword")!;
        LdapOptions current = (LdapOptions)state.Options;
        SecretUpdate change = request.Secrets?.GetValueOrDefault(bindPassword.Name) ?? new SecretUpdate(SecretAction.Keep, null);
        bool sameServer = LdapSettingsSection.Destination
            .Select(path => definition.FieldOf(path)!)
            .All(field => SettingsJson.Same(SettingsJson.Get(values, field), SettingsJson.Get(state.Values, field)));

        string? password = state.IsLocked(bindPassword)
            ? current.BindPassword
            : change.Action switch
            {
                SecretAction.Set => change.Value,
                SecretAction.Clear => string.Empty,
                _ when sameServer || string.IsNullOrEmpty(current.BindPassword) => current.BindPassword,
                _ => null,
            };

        if (password is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [bindPassword.Name] = ["Enter it again for the new server: a stored secret goes only to the server it was entered for."],
            });
        }

        LdapOptions candidate = definition.ReadOptions(values, new Dictionary<string, string?> { [bindPassword.Name] = password });
        string? userName = string.IsNullOrWhiteSpace(request.UserName) ? null : request.UserName.Trim();
        string? userPassword = string.IsNullOrEmpty(request.Password) ? null : request.Password;
        DdtUser? account = userName is null ? null : await users.FindByNameAsync(userName).ConfigureAwait(false);

        if (account is not null && userPassword is not null)
        {
            string? refusal = account switch
            {
                { Source: not AccountSource.Directory } => $"{userName} is not a directory account, so its password is not sent to the directory.",
                { IsDisabled: true } => $"{userName} is disabled, so a sign-in is refused before the directory is asked.",
                _ when await users.IsLockedOutAsync(account).ConfigureAwait(false) => $"{userName} is locked out, so a sign-in is refused before the directory is asked.",
                _ => null,
            };

            if (refusal is not null)
            {
                LdapTestOutcome bound = await tester.TestAsync(candidate, null, null, cancellationToken).ConfigureAwait(false);

                return TypedResults.Ok(new LdapTestResult(bound.Bound, null, null, [], null, bound.Bound ? refusal : bound.Message, null));
            }
        }

        LdapTestOutcome outcome = await tester.TestAsync(candidate, userName, userPassword, cancellationToken).ConfigureAwait(false);

        if (account is not null && outcome.PasswordAccepted == false)
        {
            await users.AccessFailedAsync(account).ConfigureAwait(false);
        }

        GroupRoles mapped = GroupRoles.From(outcome.Groups, candidate.GroupRoleMap);
        string? role = mapped.Decides ? mapped.Role : null;
        string message = mapped.Decides && outcome.UserFound == true
            ? $"{outcome.Message} {(role is null ? "The group map gives no role, so a sign-in is refused." : $"The group map makes the account {role}.")}"
            : outcome.Message;

        // Proof that these values keep the administrator who tests them one: a directory account needs it to save them.
        string? proof = Principals.UserId(principal) is { } callerId
            && outcome.PasswordAccepted == true
            && string.Equals(principal.Identity?.Name, userName, StringComparison.OrdinalIgnoreCase)
            && (role == DdtRoleNames.Administrator || (!mapped.Decides && principal.IsInRole(DdtRoleNames.Administrator)))
                ? proofs.Issue(callerId, candidate)
                : null;

        return TypedResults.Ok(new LdapTestResult(outcome.Bound, outcome.UserFound, outcome.PasswordAccepted, outcome.Groups, role, message, proof));
    }

    private static async Task<IResult> TestOidcAsync(
        OidcTestRequest request,
        HttpContext context,
        IHttpClientFactory clients,
        CancellationToken cancellationToken)
    {
        string redirectUri = $"{context.Request.Scheme}://{context.Request.Host}{OidcSchemeOptions.CallbackPath}";

        if (!Uri.TryCreate(request?.Authority?.Trim(), UriKind.Absolute, out Uri? authority) || authority.Scheme != Uri.UriSchemeHttps)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["authority"] = ["Enter the provider's https address, such as https://login.example.com/realms/ddt."],
            });
        }

        Uri discovery = new(authority.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");

        try
        {
            using HttpClient client = clients.CreateClient(SettingsServiceCollectionExtensions.OidcTestClient);
            using HttpResponseMessage response = await client.GetAsync(discovery, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return TypedResults.Ok(new OidcTestResult(false, null, redirectUri, $"{discovery} answered {(int)response.StatusCode} {response.ReasonPhrase}."));
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            string? issuer = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("issuer", out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;

            if (issuer is null)
            {
                return TypedResults.Ok(new OidcTestResult(false, null, redirectUri, $"{discovery} is not the discovery document of an OpenID Connect provider."));
            }

            // Tokens name their issuer, and the handler refuses one that differs from the authority it was given.
            string message = string.Equals(issuer.TrimEnd('/'), authority.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal)
                ? $"The provider answered as {issuer}. Register {redirectUri} as the redirect URI of DDT's client there."
                : $"The provider names itself {issuer}, not {authority}. Enter {issuer} as the authority.";

            return TypedResults.Ok(new OidcTestResult(true, issuer, redirectUri, message));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return TypedResults.Ok(new OidcTestResult(false, null, redirectUri, $"{discovery} could not be read: {exception.Message}"));
        }
    }

    private static async Task<Ok<IReadOnlyList<PxeHostInterfaces>>> ReadPxeInterfacesAsync(SettingsViews views, CancellationToken cancellationToken) =>
        TypedResults.Ok(await views.PxeInterfacesAsync(cancellationToken).ConfigureAwait(false));

    // The section as it is, with a new version, so every pxe host applies it again and scans its interfaces anew.
    private static async Task<IResult> RescanAsync(
        HttpContext context,
        ClaimsPrincipal user,
        SettingsStore store,
        DdtSettings settings,
        SettingsViews views,
        SettingsHostStates hostStates,
        LiveNotifier live,
        CancellationToken cancellationToken)
    {
        SettingsSectionApi<PxeSettings> api = SettingsApi.Pxe;
        long loaded = settings.Current[api.Name].Version;
        SettingsSaveResult result = await store
            .TouchAsync(api.Definition, Actor(user, context), "Asked every pxe host to scan its interfaces and apply pxe again.", cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            SettingsSaveOutcome.Saved => await SavedAsync(api, loaded, result.Snapshot ?? settings.Current, settings, views, hostStates, live, cancellationToken)
                .ConfigureAwait(false),
            SettingsSaveOutcome.KeyRingUnreadable => KeyRingUnreadable(),
            _ => Conflict(api.Name),
        };
    }

    private static SettingsActor Actor(ClaimsPrincipal user, HttpContext context) =>
        new(Principals.UserId(user), Principals.ActorName(user), context.Connection.RemoteIpAddress?.ToString());
}

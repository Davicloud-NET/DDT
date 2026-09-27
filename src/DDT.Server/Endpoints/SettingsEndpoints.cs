// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DDT.Contracts.Agents;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Ldap;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Security;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DDT.Server.Endpoints;

// The settings page. Every section needs the Administrator role, except that operators may read deployment and machines.
// A save answers with the whole section as it applies after the save, and every other browser receives the same view
// through the hub. Secrets are never sent back: only whether each is set.
public static class SettingsEndpoints
{
    // ddt-agent.exe is about 11 MB. The limit leaves room for a debug build and keeps a stray upload from filling the store.
    public const long MaxAgentBytes = 128L * 1024 * 1024;

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

        // The certificate section is read with the certificate it describes, GET /api/settings/certificate.
        MapSection(group, SettingsApi.Certificate, "/certificate/names", read: false);
        group.MapSettingsCertificateEndpoints();

        // Sends a password to the directory, so it is limited like a sign-in.
        group.MapPost("/ldap/test", TestLdapAsync).RequireAuthorization(DdtPolicies.Administrator).RequireRateLimiting(RateLimitPolicies.SignIn);
        group.MapPost("/oidc/test", TestOidcAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapGet("/pxe/interfaces", ReadPxeInterfacesAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/pxe/rescan", RescanAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapGet("/agent", ReadAgentAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPut("/agent/binary", UploadAgentAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapSettingsConsoleEndpoints();

        return group;
    }

    private static void MapSection<TValues>(RouteGroupBuilder group, SettingsSectionApi<TValues> api, string? route = null, bool read = true)
        where TValues : class
    {
        route ??= "/" + api.Name;

        if (read)
        {
            group.MapGet(route, (DdtSettings settings, SettingsViews views, CancellationToken cancellationToken) =>
                    ReadAsync(api, settings, views, cancellationToken))
                .RequireAuthorization(api.OperatorsMayRead ? DdtPolicies.Operator : DdtPolicies.Administrator);
        }

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
            return ServerProblems.Validation("values", ServerMessages.SettingsSendValues.With());
        }

        if (update.Secrets?.Keys.FirstOrDefault(name => api.Definition.Field(name) is not { IsSecret: true }) is { } unknown)
        {
            return ServerProblems.Validation(unknown, ServerMessages.SettingsNoSuchSecret.With("section", api.Name, "name", unknown));
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
        ServerProblems.Problem(ServerMessages.SettingsSavedSince.With("section", section), StatusCodes.Status409Conflict);

    // Fields names what needs the fresh proof, so the page can say what the password is for.
    internal static ProblemHttpResult Reauthenticate(IReadOnlyList<string> fields) =>
        ServerProblems.Problem(
            ServerMessages.SettingsEnterPasswordAgain.With("fields", string.Join(", ", fields)),
            StatusCodes.Status403Forbidden,
            new Dictionary<string, object?> { ["fields"] = fields.ToArray() });

    private static ProblemHttpResult KeyRingUnreadable() =>
        ServerProblems.Problem(ServerMessages.SettingsKeyRingUnreadable.With(), StatusCodes.Status409Conflict);

    // Problems keyed by field, with their codes under errorCodes as every validation problem has them; the warnings still
    // to confirm under confirm, as "code: message", and whole in the confirm extension, so the page can ask and send the
    // codes back.
    private static ValidationProblem Invalid(SettingsSaveResult result)
    {
        List<IGrouping<string, SettingMessage>> fields = [.. (result.Problems ?? []).GroupBy(problem => problem.Field, StringComparer.Ordinal)];
        Dictionary<string, string[]> errors = fields.ToDictionary(
            group => group.Key,
            group => group.Select(problem => problem.Message).ToArray(),
            StringComparer.Ordinal);
        Dictionary<string, ServerMessage?[]> codes = fields.ToDictionary(
            group => group.Key,
            group => group.Select(problem => problem.Text).ToArray(),
            StringComparer.Ordinal);

        IReadOnlyList<SettingMessage> unconfirmed = result.Unconfirmed ?? [];
        Dictionary<string, object?> extensions = new(StringComparer.Ordinal) { [ServerProblems.ErrorCodesExtension] = codes };

        if (unconfirmed.Count > 0)
        {
            errors["confirm"] = [.. unconfirmed.Select(warning => $"{warning.Code}: {warning.Message}")];
            extensions["confirm"] = unconfirmed.ToArray();
        }

        return TypedResults.ValidationProblem(errors, extensions: extensions);
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
            return ServerProblems.Problem(ServerMessages.SettingsReauthenticateNoPassword.With(), StatusCodes.Status403Forbidden);
        }

        (SignInResult result, DdtUser? verified) = await credentials
            .VerifyAsync(user.UserName, request?.Password ?? string.Empty, request?.Code, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsLockedOut)
        {
            return ServerProblems.Problem(ServerMessages.SettingsReauthenticateLockedOut.With(), StatusCodes.Status403Forbidden);
        }

        if (result.RequiresTwoFactor)
        {
            return ServerProblems.Validation("code", ServerMessages.SettingsReauthenticateCodeNeeded.With());
        }

        if (!result.Succeeded || verified?.Id != user.Id)
        {
            return ServerProblems.Validation("password", ServerMessages.SettingsReauthenticateNotRight.With());
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
            return ServerProblems.Validation("values", ServerMessages.SettingsLdapTestSendValues.With());
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
            return ServerProblems.Validation(bindPassword.Name, ServerMessages.SettingsSecretForNewServer.With());
        }

        LdapOptions candidate = definition.ReadOptions(values, new Dictionary<string, string?> { [bindPassword.Name] = password });
        string? userName = string.IsNullOrWhiteSpace(request.UserName) ? null : request.UserName.Trim();
        string? userPassword = string.IsNullOrEmpty(request.Password) ? null : request.Password;
        DdtUser? account = userName is null ? null : await users.FindByNameAsync(userName).ConfigureAwait(false);

        if (account is not null && userPassword is not null)
        {
            ServerMessage? refusal = account switch
            {
                { Source: not AccountSource.Directory } => ServerMessages.SettingsLdapTestNotDirectoryAccount.With("name", userName!),
                { IsDisabled: true } => ServerMessages.SettingsLdapTestAccountDisabled.With("name", userName!),
                _ when await users.IsLockedOutAsync(account).ConfigureAwait(false) => ServerMessages.SettingsLdapTestLockedOut.With("name", userName!),
                _ => null,
            };

            if (refusal is not null)
            {
                LdapTestOutcome bound = await tester.TestAsync(candidate, null, null, cancellationToken).ConfigureAwait(false);
                ServerMessage said = bound.Bound ? refusal : bound.Text;

                return TypedResults.Ok(new LdapTestResult(bound.Bound, null, null, [], null, said.Text, null, said));
            }
        }

        LdapTestOutcome outcome = await tester.TestAsync(candidate, userName, userPassword, cancellationToken).ConfigureAwait(false);

        if (account is not null && outcome.PasswordAccepted == false)
        {
            await users.AccessFailedAsync(account).ConfigureAwait(false);
        }

        GroupRoles mapped = GroupRoles.From(outcome.Groups, candidate.GroupRoleMap);
        string? role = mapped.Decides ? mapped.Role : null;
        ServerMessage message = (mapped.Decides && outcome.UserFound == true, role) switch
        {
            (false, _) => outcome.Text,
            (true, null) => ServerMessages.SettingsLdapTestNoRole.With("result", outcome.Text),
            (true, { } given) => ServerMessages.SettingsLdapTestRole.With("result", outcome.Text, "role", DirectorySignInService.RoleName(given)),
        };

        // Proof that these values keep the administrator who tests them one: a directory account needs it to save them.
        string? proof = Principals.UserId(principal) is { } callerId
            && outcome.PasswordAccepted == true
            && string.Equals(principal.Identity?.Name, userName, StringComparison.OrdinalIgnoreCase)
            && (role == DdtRoleNames.Administrator || (!mapped.Decides && principal.IsInRole(DdtRoleNames.Administrator)))
                ? proofs.Issue(callerId, candidate)
                : null;

        return TypedResults.Ok(new LdapTestResult(outcome.Bound, outcome.UserFound, outcome.PasswordAccepted, outcome.Groups, role, message.Text, proof, message));
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
            return ServerProblems.Validation("authority", ServerMessages.SettingsOidcTestAuthorityInvalid.With());
        }

        Uri discovery = new(authority.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
        string url = discovery.ToString();

        OidcTestResult Result(bool reached, string? issuer, ServerMessage message) => new(reached, issuer, redirectUri, message.Text, message);

        try
        {
            using HttpClient client = clients.CreateClient(SettingsServiceCollectionExtensions.OidcTestClient);
            using HttpResponseMessage response = await client.GetAsync(discovery, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return TypedResults.Ok(Result(
                    false,
                    null,
                    ServerMessages.SettingsOidcTestAnswered.With("url", url, "status", (int)response.StatusCode, "reason", response.ReasonPhrase ?? string.Empty)));
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
                return TypedResults.Ok(Result(false, null, ServerMessages.SettingsOidcTestNotDiscovery.With("url", url)));
            }

            // Tokens name their issuer, and the handler refuses one that differs from the authority it was given.
            ServerMessage message = string.Equals(issuer.TrimEnd('/'), authority.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal)
                ? ServerMessages.SettingsOidcTestReached.With("issuer", issuer, "redirectUri", redirectUri)
                : ServerMessages.SettingsOidcTestOtherIssuer.With("issuer", issuer, "authority", authority.ToString());

            return TypedResults.Ok(Result(true, issuer, message));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return TypedResults.Ok(Result(false, null, ServerMessages.SettingsOidcTestUnreadable.With("url", url, "error", exception.Message)));
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

    private static async Task<Ok<AgentBinaryView>> ReadAgentAsync(
        AgentReleaseStore releases,
        IOptions<AgentReleaseOptions> options,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        AgentRelease? release = await releases.CurrentAsync(cancellationToken).ConfigureAwait(false);
        bool configured = !string.IsNullOrWhiteSpace(options.Value.BinaryPath);

        AuditEvent? upload = configured || release is null
            ? null
            : await database.AuditEvents
                .AsNoTracking()
                .Where(audit => audit.Action == AuditActions.AgentUploaded)
                .OrderByDescending(audit => audit.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        return TypedResults.Ok(new AgentBinaryView(
            release?.Sha256,
            release?.Size,
            upload?.OccurredUtc,
            upload?.ActorName,
            configured ? AgentBinarySource.Configuration : release is null ? AgentBinarySource.None : AgentBinarySource.Uploaded));
    }

    // Every netbooting machine runs this as SYSTEM before anyone authorized it, and the agent checks only that it got what
    // the server announced, so the upload needs a fresh proof of identity. Written next to the agent it replaces and
    // renamed over it, so a machine never downloads half a file.
    private static async Task<IResult> UploadAgentAsync(
        HttpContext context,
        ClaimsPrincipal user,
        AgentReleaseStore releases,
        IOptions<AgentReleaseOptions> options,
        ReauthenticationTokens reauthentication,
        UserManager<DdtUser> users,
        DdtDbContext database,
        TimeProvider timeProvider,
        LiveNotifier live,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.BinaryPath))
        {
            return ServerProblems.Problem(ServerMessages.SettingsAgentConfigured.With(), StatusCodes.Status409Conflict);
        }

        if (!await reauthentication.ValidAsync(context, user, users).ConfigureAwait(false))
        {
            return Reauthenticate(["agent"]);
        }

        if (context.Request.ContentLength > MaxAgentBytes)
        {
            return TooLarge();
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = MaxAgentBytes;
        }

        string path = releases.BinaryPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = $"{path}.{Guid.NewGuid():N}.upload";
        string sha256;
        long size = 0;

        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] header = new byte[2];
            byte[] buffer = new byte[81920];

            await using (FileStream file = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
            {
                int read;

                while ((read = await context.Request.Body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    for (int index = 0; index < read && size + index < header.Length; index++)
                    {
                        header[size + index] = buffer[index];
                    }

                    size += read;

                    if (size > MaxAgentBytes)
                    {
                        return TooLarge();
                    }

                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            // Every Windows executable starts with the DOS header's MZ.
            if (size < header.Length || header[0] != (byte)'M' || header[1] != (byte)'Z')
            {
                return ServerProblems.Validation("binary", ServerMessages.SettingsAgentNotExecutable.With());
            }

            sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }

        AuditEvent audit = new()
        {
            OccurredUtc = timeProvider.GetUtcNow(),
            Action = AuditActions.AgentUploaded,
            ActorUserId = Principals.UserId(user),
            ActorName = Principals.ActorName(user),
            SubjectId = sha256,
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = $"Uploaded the agent with SHA-256 {sha256}, {size} bytes. Netbooting machines run it from their next boot.",
        };

        database.AuditEvents.Add(audit);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // As GET /api/settings/agent reads it from now on, so the page shows the answer without reading it again, and
        // other administrators' pages take it from the hub.
        AgentBinaryView uploaded = new(sha256, size, audit.OccurredUtc, audit.ActorName, AgentBinarySource.Uploaded);
        live.AgentChanged(uploaded);

        return TypedResults.Ok(uploaded);
    }

    private static ProblemHttpResult TooLarge() =>
        ServerProblems.Problem(ServerMessages.SettingsAgentTooLarge.With("max", MaxAgentBytes / (1024 * 1024)), StatusCodes.Status413PayloadTooLarge);

    internal static SettingsActor Actor(ClaimsPrincipal user, HttpContext context) =>
        new(Principals.UserId(user), Principals.ActorName(user), context.Connection.RemoteIpAddress?.ToString());
}

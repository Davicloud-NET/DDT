// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Data;
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
// Secrets are never sent back: only whether each is set.
public static class SettingsEndpoints
{
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

        group.MapSettingsTestEndpoints();
        group.MapGet("/pxe/interfaces", ReadPxeInterfacesAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/pxe/rescan", RescanAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapSettingsAgentEndpoints();
        group.MapSettingsConsoleEndpoints();
        group.MapSettingsConsoleLogoEndpoints();

        return group;
    }

    // Fields names what needs the fresh proof, so the page can say what the password is for.
    internal static ProblemHttpResult Reauthenticate(IReadOnlyList<string> fields) =>
        ServerProblems.Problem(
            ServerMessages.SettingsEnterPasswordAgain.With("fields", string.Join(", ", fields)),
            StatusCodes.Status403Forbidden,
            new Dictionary<string, object?> { ["fields"] = fields.ToArray() });

    // Settings name who changed them with the API token beside the user, such as alice (token build-server).
    internal static Actor SettingsActor(HttpContext context) => Actor.Of(context) with { Name = Principals.ActorName(context.User) };

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
                [AsParameters] SettingsCaller caller,
                SettingsSaves saves,
                CancellationToken cancellationToken) =>
                SaveAsync(api, update, caller, saves, cancellationToken))
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
        SettingsCaller caller,
        SettingsSaves saves,
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
            Reauthenticated = await caller.ReauthenticatedAsync().ConfigureAwait(false),
            DirectoryProof = caller.Context.Request.Headers[DirectoryProofs.HeaderName],
        };

        SettingsSaved<TValues> saved = await saves.SaveAsync(api, change, caller.Actor, cancellationToken).ConfigureAwait(false);

        return saved switch
        {
            { View: { } view } => TypedResults.Ok(view),
            { Result.Outcome: SettingsSaveOutcome.Conflict } => Conflict(api.Name),
            { Result.Outcome: SettingsSaveOutcome.Reauthenticate } => Reauthenticate(saved.Result.Fields ?? []),
            { Result.Outcome: SettingsSaveOutcome.KeyRingUnreadable } => KeyRingUnreadable(),
            _ => Invalid(saved.Result),
        };
    }

    private static ProblemHttpResult Conflict(string section) =>
        ServerProblems.Problem(ServerMessages.SettingsSavedSince.With("section", section), StatusCodes.Status409Conflict);

    private static ProblemHttpResult KeyRingUnreadable() =>
        ServerProblems.Problem(ServerMessages.SettingsKeyRingUnreadable.With(), StatusCodes.Status409Conflict);

    // Problems by field, with their codes under errorCodes as in every validation problem. Warnings still to confirm go
    // under confirm as "code: message", and whole in the confirm extension, for the page to ask and send the codes
    // back.
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
        [AsParameters] SettingsCaller caller,
        CredentialVerifier credentials,
        CancellationToken cancellationToken)
    {
        (HttpContext context, ReauthenticationTokens tokens, UserManager<DdtUser> users) = caller;
        DdtUser? user = await users.GetUserAsync(context.User).ConfigureAwait(false);

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

    private static async Task<Ok<IReadOnlyList<PxeHostInterfaces>>> ReadPxeInterfacesAsync(SettingsViews views, CancellationToken cancellationToken) =>
        TypedResults.Ok(await views.PxeInterfacesAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RescanAsync(HttpContext context, SettingsSaves saves, CancellationToken cancellationToken)
    {
        SettingsSaved<PxeSettings> saved = await saves.RescanAsync(SettingsActor(context), cancellationToken).ConfigureAwait(false);

        return saved switch
        {
            { View: { } view } => TypedResults.Ok(view),
            { Result.Outcome: SettingsSaveOutcome.KeyRingUnreadable } => KeyRingUnreadable(),
            _ => Conflict(SettingsApi.Pxe.Name),
        };
    }
}

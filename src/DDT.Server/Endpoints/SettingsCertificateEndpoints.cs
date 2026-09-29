// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Certificates;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// The server certificate on the settings page. These endpoints act on the process that serves the page.
public static class SettingsCertificateEndpoints
{
    public static RouteGroupBuilder MapSettingsCertificateEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/certificate", ReadAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/certificate", UploadAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/certificate/generate", GenerateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/certificate/confirm", ConfirmAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<CertificateView>> ReadAsync(HttpContext context, CertificateChanges changes, CancellationToken cancellationToken) =>
        TypedResults.Ok(await changes.ViewAsync(ServerCertificateExtensions.ServedThumbprint(context), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> UploadAsync(
        CertificateUpload upload,
        [AsParameters] SettingsCaller caller,
        CertificateChanges changes,
        CancellationToken cancellationToken)
    {
        if (!changes.Manageable)
        {
            return NotManageable();
        }

        if (!await caller.ReauthenticatedAsync().ConfigureAwait(false))
        {
            return SettingsEndpoints.Reauthenticate(["certificate"]);
        }

        HttpContext context = caller.Context;
        CertificateChange change = await changes
            .UploadAsync(upload, context.Request.Host.Host, ServerCertificateExtensions.ServedThumbprint(context), caller.Actor, cancellationToken)
            .ConfigureAwait(false);

        return Answer(change);
    }

    private static async Task<IResult> GenerateAsync(
        CertificateGenerate? generate,
        [AsParameters] SettingsCaller caller,
        CertificateChanges changes,
        CancellationToken cancellationToken)
    {
        if (!changes.Manageable)
        {
            return NotManageable();
        }

        if (!changes.CanGenerate)
        {
            return ServerProblems.Problem(ServerMessages.SettingsCertificateGenerateOff.With(), StatusCodes.Status409Conflict);
        }

        if (!await caller.ReauthenticatedAsync().ConfigureAwait(false))
        {
            return SettingsEndpoints.Reauthenticate(["certificate"]);
        }

        HttpContext context = caller.Context;
        CertificateChange change = await changes
            .GenerateAsync(generate?.Confirm, context.Request.Host.Host, ServerCertificateExtensions.ServedThumbprint(context), caller.Actor, cancellationToken)
            .ConfigureAwait(false);

        return Answer(change);
    }

    private static async Task<IResult> ConfirmAsync(HttpContext context, CertificateChanges changes, CancellationToken cancellationToken)
    {
        if (!changes.Manageable)
        {
            return NotManageable();
        }

        CertificateChange change = await changes
            .ConfirmAsync(ServerCertificateExtensions.ServedThumbprint(context), SettingsEndpoints.SettingsActor(context), cancellationToken)
            .ConfigureAwait(false);

        return Answer(change);
    }

    private static IResult Answer(CertificateChange change) => change switch
    {
        { View: { } view } => TypedResults.Ok(view),
        { Problem: { } problem } => ServerProblems.Validation(problem.Field, problem.Text),
        { NewRoot: { } message } => NewRoot(message),
        { Refusal: { } refusal } => ServerProblems.Problem(refusal, StatusCodes.Status409Conflict),
        _ => throw new UnreachableException(),
    };

    private static ProblemHttpResult NotManageable() =>
        ServerProblems.Problem(ServerMessages.SettingsCertificateNotManageable.With(), StatusCodes.Status409Conflict);

    // Answers like a save's warning that needs confirming. It goes under confirm as "code: message", and as a whole in
    // the confirm extension.
    private static ValidationProblem NewRoot(ServerMessage message) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["confirm"] = [$"{SettingWarningCodes.CertificateNewRoot}: {message.Text}"] },
            extensions: new Dictionary<string, object?>
            {
                ["confirm"] = new[] { new SettingMessage(string.Empty, message.Text, SettingWarningCodes.CertificateNewRoot, message) },
            });
}

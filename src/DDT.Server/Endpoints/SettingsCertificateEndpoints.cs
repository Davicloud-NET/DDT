// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Certificates;
using DDT.Server.Configuration;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Endpoints;

// The server certificate on the settings page: upload a pair, or generate one from DDT's root for the server names. A
// new pair is served at once but provisionally, and goes back to the pair before it unless someone confirms it within 5
// minutes from a connection that was served it. These act on the process that serves the page.
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

    public static async Task<CertificateView> ViewAsync(
        ServerCertificates? certificates,
        SettingsViews views,
        DdtSettings settings,
        HttpContext? context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(settings);

        SettingsSectionView<CertificateSettings> names = await views.ViewAsync(SettingsApi.Certificate, settings.Current, cancellationToken).ConfigureAwait(false);
        string? served = context is null ? null : ServerCertificateExtensions.ServedThumbprint(context);

        return new CertificateView(
            certificates is not null,
            certificates is null ? ServerMessages.SettingsCertificateNotManageable.With().Text : null,
            certificates?.Describe(),
            certificates?.Provisional?.DeadlineUtc,
            served is null ? null : string.Equals(served, certificates?.Current?.Thumbprint, StringComparison.OrdinalIgnoreCase),
            certificates?.CanGenerate ?? false,
            certificates?.HasRoot ?? false,
            names);
    }

    private static async Task<Ok<CertificateView>> ReadAsync(
        HttpContext context,
        SettingsViews views,
        DdtSettings settings,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await ViewAsync(context.RequestServices.GetService<ServerCertificates>(), views, settings, context, cancellationToken).ConfigureAwait(false));

    // The pair has to load with its key, be valid now, name the host of this request and every server name, and a pair
    // that does not come from DDT's root, which boot images pin, needs the confirmation certificate.newRoot.
    private static async Task<IResult> UploadAsync(
        CertificateUpload upload,
        HttpContext context,
        ClaimsPrincipal user,
        DdtSettings settings,
        SettingsViews views,
        ReauthenticationTokens reauthentication,
        UserManager<DdtUser> users,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (context.RequestServices.GetService<ServerCertificates>() is not { } certificates)
        {
            return NotManageable();
        }

        if (!await reauthentication.ValidAsync(context, user, users).ConfigureAwait(false))
        {
            return SettingsEndpoints.Reauthenticate(["certificate"]);
        }

        (PemPair? pair, string? field, ServerMessage? problem) = Read(upload);

        if (pair is null)
        {
            return ServerProblems.Validation(field!, problem!);
        }

        using X509Certificate2 certificate = X509Certificate2.CreateFromPem(pair.CertificatePem, pair.KeyPem);
        DateTimeOffset now = timeProvider.GetUtcNow();

        if (Refusal(certificate, context, settings, now) is { } refusal)
        {
            return ServerProblems.Validation("certificate", refusal);
        }

        if (!certificates.ChainsToRoot(certificate) && !Confirmed(upload.Confirm))
        {
            return NewRoot(ServerMessages.SettingsCertificateNewRootUpload.With());
        }

        CertificateCheck check = await certificates.InstallAsync(pair, cancellationToken).ConfigureAwait(false);

        return await InstalledAsync(certificates, check, "Uploaded", context, user, settings, views, database, live, timeProvider, cancellationToken)
            .ConfigureAwait(false);
    }

    // From DDT's root, for localhost, this computer and the server names. Without a root yet, Generate makes one, which
    // every boot image then has to be built again for.
    private static async Task<IResult> GenerateAsync(
        CertificateGenerate? generate,
        HttpContext context,
        ClaimsPrincipal user,
        DdtSettings settings,
        SettingsViews views,
        ReauthenticationTokens reauthentication,
        UserManager<DdtUser> users,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (context.RequestServices.GetService<ServerCertificates>() is not { } certificates)
        {
            return NotManageable();
        }

        if (!certificates.CanGenerate)
        {
            return ServerProblems.Problem(ServerMessages.SettingsCertificateGenerateOff.With(), StatusCodes.Status409Conflict);
        }

        if (!await reauthentication.ValidAsync(context, user, users).ConfigureAwait(false))
        {
            return SettingsEndpoints.Reauthenticate(["certificate"]);
        }

        IReadOnlyList<string> names = ServerNames.Required(((HttpsOptions)settings.Current[SettingsSectionNames.Certificate].Options).SubjectAlternativeNames);

        if (!Covers(names, context.Request.Host.Host))
        {
            return ServerProblems.Validation(
                "subjectAlternativeNames",
                ServerMessages.SettingsCertificateAddHostFirst.With("host", context.Request.Host.Host));
        }

        if (!certificates.HasRoot && !Confirmed(generate?.Confirm))
        {
            return NewRoot(ServerMessages.SettingsCertificateNewRootGenerate.With());
        }

        CertificateCheck check = await certificates.GenerateAsync(names, cancellationToken).ConfigureAwait(false);

        return await InstalledAsync(certificates, check, "Generated", context, user, settings, views, database, live, timeProvider, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> ConfirmAsync(
        HttpContext context,
        ClaimsPrincipal user,
        DdtSettings settings,
        SettingsViews views,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (context.RequestServices.GetService<ServerCertificates>() is not { } certificates)
        {
            return NotManageable();
        }

        string? thumbprint = certificates.Provisional?.Thumbprint;
        CertificateConfirmation confirmation = await certificates
            .ConfirmAsync(ServerCertificateExtensions.ServedThumbprint(context), cancellationToken)
            .ConfigureAwait(false);

        switch (confirmation)
        {
            case CertificateConfirmation.NothingToConfirm:
                return ServerProblems.Problem(ServerMessages.SettingsCertificateNothingToConfirm.With(), StatusCodes.Status409Conflict);

            case CertificateConfirmation.NotServedTheNewPair:
                return ServerProblems.Problem(ServerMessages.SettingsCertificateNotServedNew.With(), StatusCodes.Status409Conflict);
        }

        database.AuditEvents.Add(Audit(
            AuditActions.CertificateConfirmed,
            thumbprint!,
            SettingsEndpoints.Actor(user, context),
            timeProvider.GetUtcNow(),
            $"Confirmed the server certificate {certificates.Current?.Subject}, SHA-256 {thumbprint}, from a connection that was served it."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        live.CertificateChanged(await ViewAsync(certificates, views, settings, null, cancellationToken).ConfigureAwait(false));

        return TypedResults.Ok(await ViewAsync(certificates, views, settings, context, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IResult> InstalledAsync(
        ServerCertificates certificates,
        CertificateCheck check,
        string how,
        HttpContext context,
        ClaimsPrincipal user,
        DdtSettings settings,
        SettingsViews views,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        X509Certificate2 installed = check.Certificate;

        database.AuditEvents.Add(Audit(
            AuditActions.CertificateReplaced,
            installed.Thumbprint,
            SettingsEndpoints.Actor(user, context),
            timeProvider.GetUtcNow(),
            $"{how} the server certificate {installed.Subject} for {string.Join(", ", ServerNames.Of(installed))}, issued by " +
            $"{installed.Issuer}, valid until {installed.NotAfter.ToUniversalTime():u}. It is provisional until " +
            $"{certificates.Provisional!.DeadlineUtc:u}, and DDT goes back to the certificate before unless it is confirmed."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        live.CertificateChanged(await ViewAsync(certificates, views, settings, null, cancellationToken).ConfigureAwait(false));

        return TypedResults.Ok(await ViewAsync(certificates, views, settings, context, cancellationToken).ConfigureAwait(false));
    }

    // A PEM chain with its key, or a PFX with its password, as the pair of PEM files DDT keeps.
    private static (PemPair? Pair, string? Field, ServerMessage? Problem) Read(CertificateUpload? upload)
    {
        if (upload is null)
        {
            return (null, "certificatePem", ServerMessages.SettingsCertificateSendPair.With());
        }

        if (!string.IsNullOrWhiteSpace(upload.Pfx))
        {
            byte[] pfx;

            try
            {
                pfx = Convert.FromBase64String(upload.Pfx);
            }
            catch (FormatException)
            {
                return (null, "pfx", ServerMessages.SettingsCertificateNotBase64.With());
            }

            try
            {
                X509Certificate2Collection collection = X509CertificateLoader.LoadPkcs12Collection(pfx, upload.PfxPassword, X509KeyStorageFlags.Exportable);

                try
                {
                    if (collection.FirstOrDefault(certificate => certificate.HasPrivateKey) is not { } leaf)
                    {
                        return (null, "pfx", ServerMessages.SettingsCertificatePfxWithoutKey.With());
                    }

                    string? key = leaf.GetRSAPrivateKey()?.ExportPkcs8PrivateKeyPem() ?? leaf.GetECDsaPrivateKey()?.ExportPkcs8PrivateKeyPem();

                    if (key is null)
                    {
                        return (null, "pfx", ServerMessages.SettingsCertificateKeyAlgorithm.With());
                    }

                    string chain = string.Join("\n", [leaf.ExportCertificatePem(), .. collection.Where(other => other != leaf).Select(other => other.ExportCertificatePem())]);

                    return (new PemPair(chain + "\n", key + "\n"), null, null);
                }
                finally
                {
                    foreach (X509Certificate2 certificate in collection)
                    {
                        certificate.Dispose();
                    }
                }
            }
            catch (CryptographicException exception)
            {
                return (null, "pfx", ServerMessages.SettingsCertificatePfxPassword.With("error", exception.Message));
            }
        }

        if (string.IsNullOrWhiteSpace(upload.CertificatePem) || string.IsNullOrWhiteSpace(upload.KeyPem))
        {
            return (null, "certificatePem", ServerMessages.SettingsCertificateSendPem.With());
        }

        try
        {
            using X509Certificate2 pair = X509Certificate2.CreateFromPem(upload.CertificatePem, upload.KeyPem);

            return (new PemPair(upload.CertificatePem.Trim() + "\n", upload.KeyPem.Trim() + "\n"), null, null);
        }
        catch (CryptographicException exception)
        {
            return (null, "keyPem", ServerMessages.SettingsCertificateKeyMismatch.With("error", exception.Message));
        }
    }

    // Valid now, and for every name the page is reached by: the host of this request and the server names.
    private static ServerMessage? Refusal(X509Certificate2 certificate, HttpContext context, DdtSettings settings, DateTimeOffset now)
    {
        if (now < new DateTimeOffset(certificate.NotBefore.ToUniversalTime()) || now > new DateTimeOffset(certificate.NotAfter.ToUniversalTime()))
        {
            return ServerMessages.SettingsCertificateNotValidNow.With(
                "from",
                certificate.NotBefore.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture),
                "until",
                certificate.NotAfter.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture));
        }

        IReadOnlyList<string> serverNames = CertificateSettingsSection.Names(
            ((HttpsOptions)settings.Current[SettingsSectionNames.Certificate].Options).SubjectAlternativeNames);
        string[] missing = [.. new[] { context.Request.Host.Host }.Concat(serverNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !ServerNames.Covers(certificate, [name]))];

        return missing.Length == 0
            ? null
            : ServerMessages.SettingsCertificateMissingNames.With("names", string.Join(", ", missing));
    }

    private static bool Covers(IReadOnlyList<string> names, string host) =>
        names.Contains(host, StringComparer.OrdinalIgnoreCase)
        || ServerNames.LocalAddresses().Any(address => string.Equals(address.ToString(), host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase));

    private static bool Confirmed(IReadOnlyList<string>? confirm) =>
        confirm?.Contains(SettingWarningCodes.CertificateNewRoot, StringComparer.Ordinal) == true;

    private static ProblemHttpResult NotManageable() =>
        ServerProblems.Problem(ServerMessages.SettingsCertificateNotManageable.With(), StatusCodes.Status409Conflict);

    // As a save's warning to confirm: under confirm as "code: message", and whole in the confirm extension.
    private static ValidationProblem NewRoot(ServerMessage message) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { ["confirm"] = [$"{SettingWarningCodes.CertificateNewRoot}: {message.Text}"] },
            extensions: new Dictionary<string, object?>
            {
                ["confirm"] = new[] { new SettingMessage(string.Empty, message.Text, SettingWarningCodes.CertificateNewRoot, message) },
            });

    private static AuditEvent Audit(string action, string subject, SettingsActor actor, DateTimeOffset now, string detail) => new()
    {
        OccurredUtc = now,
        Action = action,
        ActorUserId = actor.UserId,
        ActorName = actor.Name,
        SubjectId = subject,
        SourceAddress = actor.Address,
        Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
    };
}

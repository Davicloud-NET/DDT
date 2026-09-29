// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using DDT.Server.Certificates;
using DDT.Server.Configuration;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;

namespace DDT.Server.Settings;

// Changes the server certificate from the settings page. The page uploads a pair or generates one from DDT's root for
// the server names. Certificates is null when Kestrel loads the certificate by itself.
internal sealed class CertificateChanges(
    DdtSettings settings,
    SettingsViews views,
    DdtDbContext database,
    LiveNotifier live,
    TimeProvider timeProvider,
    ServerCertificates? certificates = null)
{
    public bool Manageable => certificates is not null;

    public bool CanGenerate => certificates?.CanGenerate ?? false;

    private ServerCertificates Managed => certificates ?? throw new InvalidOperationException("Kestrel loads the server certificate on its own.");

    // Served is the thumbprint of the certificate this request's connection was served. It's null for a push.
    public async Task<CertificateView> ViewAsync(string? served, CancellationToken cancellationToken)
    {
        SettingsSectionView<CertificateSettings> names = await views.ViewAsync(SettingsApi.Certificate, settings.Current, cancellationToken).ConfigureAwait(false);

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

    // The pair has to load with its key, be valid now, and name the host of this request and every server name. Boot
    // images pin DDT's root, so a pair that doesn't chain to it needs the confirmation certificate.newRoot.
    public async Task<CertificateChange> UploadAsync(CertificateUpload? upload, string host, string? served, Actor actor, CancellationToken cancellationToken)
    {
        (PemPair? pair, SettingProblem? problem) = CertificateUploads.Read(upload);

        if (pair is null)
        {
            return new CertificateChange { Problem = problem };
        }

        using X509Certificate2 certificate = X509Certificate2.CreateFromPem(pair.CertificatePem, pair.KeyPem);

        if (Refusal(certificate, host, timeProvider.GetUtcNow()) is { } refusal)
        {
            return new CertificateChange { Problem = new SettingProblem("certificate", refusal) };
        }

        if (!Managed.ChainsToRoot(certificate) && !Confirmed(upload?.Confirm))
        {
            return new CertificateChange { NewRoot = ServerMessages.SettingsCertificateNewRootUpload.With() };
        }

        CertificateCheck check = await Managed.InstallAsync(pair, cancellationToken).ConfigureAwait(false);

        return new CertificateChange { View = await InstalledAsync(check, "Uploaded", served, actor, cancellationToken).ConfigureAwait(false) };
    }

    // Issues a pair from DDT's root for localhost, this computer and the server names. If there's no root yet, this
    // creates one, and every boot image then has to be rebuilt.
    public async Task<CertificateChange> GenerateAsync(IReadOnlyList<string>? confirm, string host, string? served, Actor actor, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> names = ServerNames.Required(((HttpsOptions)settings.Current[SettingsSectionNames.Certificate].Options).SubjectAlternativeNames);

        if (!Covers(names, host))
        {
            return new CertificateChange
            {
                Problem = new SettingProblem("subjectAlternativeNames", ServerMessages.SettingsCertificateAddHostFirst.With("host", host)),
            };
        }

        if (!Managed.HasRoot && !Confirmed(confirm))
        {
            return new CertificateChange { NewRoot = ServerMessages.SettingsCertificateNewRootGenerate.With() };
        }

        CertificateCheck check = await Managed.GenerateAsync(names, cancellationToken).ConfigureAwait(false);

        return new CertificateChange { View = await InstalledAsync(check, "Generated", served, actor, cancellationToken).ConfigureAwait(false) };
    }

    // Confirms the provisional pair only from a connection that was served it, which proves its browser accepted it.
    // Any other connection gets a refusal.
    public async Task<CertificateChange> ConfirmAsync(string? served, Actor actor, CancellationToken cancellationToken)
    {
        string? thumbprint = Managed.Provisional?.Thumbprint;
        CertificateConfirmation confirmation = await Managed.ConfirmAsync(served, cancellationToken).ConfigureAwait(false);

        switch (confirmation)
        {
            case CertificateConfirmation.NothingToConfirm:
                return new CertificateChange { Refusal = ServerMessages.SettingsCertificateNothingToConfirm.With() };

            case CertificateConfirmation.NotServedTheNewPair:
                return new CertificateChange { Refusal = ServerMessages.SettingsCertificateNotServedNew.With() };
        }

        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.CertificateConfirmed,
            thumbprint,
            actor,
            timeProvider.GetUtcNow(),
            $"Confirmed the server certificate {Managed.Current?.Subject}, SHA-256 {thumbprint}, from a connection that was served it."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new CertificateChange { View = await PushAsync(served, cancellationToken).ConfigureAwait(false) };
    }

    // Records that every boot image was rebuilt with DDT's root, so none pins the certificate the root replaced.
    // Returns false when no boot image is waiting for a rebuild.
    public async Task<bool> AcknowledgeReplacedAnchorAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (certificates?.ReplacedAnchorSha256() is not { } sha256)
        {
            return false;
        }

        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.CertificateAnchorAcknowledged,
            sha256,
            actor,
            timeProvider.GetUtcNow(),
            $"Confirmed that every boot image was built again with DDT's root, so none pins the replaced certificate, SHA-256 {sha256}."));

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        certificates.ForgetReplacedAnchor();

        return true;
    }

    private async Task<CertificateView> InstalledAsync(CertificateCheck check, string how, string? served, Actor actor, CancellationToken cancellationToken)
    {
        X509Certificate2 installed = check.Certificate;

        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.CertificateReplaced,
            installed.Thumbprint,
            actor,
            timeProvider.GetUtcNow(),
            $"{how} the server certificate {installed.Subject} for {string.Join(", ", ServerNames.Of(installed))}, issued by " +
            $"{installed.Issuer}, valid until {installed.NotAfter.ToUniversalTime():u}. It is provisional until " +
            $"{Managed.Provisional!.DeadlineUtc:u}, and DDT goes back to the certificate before unless it is confirmed."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await PushAsync(served, cancellationToken).ConfigureAwait(false);
    }

    // Other pages get the view without ServedHere. Only this request's connection knows which pair it was served.
    private async Task<CertificateView> PushAsync(string? served, CancellationToken cancellationToken)
    {
        live.CertificateChanged(await ViewAsync(null, cancellationToken).ConfigureAwait(false));

        return await ViewAsync(served, cancellationToken).ConfigureAwait(false);
    }

    // The certificate has to be valid now and cover every name the page is reached by. Those are the host of this
    // request and the server names.
    private ServerMessage? Refusal(X509Certificate2 certificate, string host, DateTimeOffset now)
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
        string[] missing = [.. new[] { host }.Concat(serverNames)
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
}

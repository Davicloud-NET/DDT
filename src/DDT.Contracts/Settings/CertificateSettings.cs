// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Server;

namespace DDT.Contracts.Settings;

// The section certificate: the names the server is reached by, which Generate issues for and an upload has to cover.
public sealed record CertificateSettings(IReadOnlyList<string> SubjectAlternativeNames);

// Manageable is false, with the reason in NotManageable, when the page cannot change the certificate. Served is the
// certificate every new connection gets. ProvisionalUntil is set while a new pair waits for its confirmation, which has
// to come from a connection that was served it, or DDT goes back to the pair before it then. ServedHere says whether the
// connection of this request was served the certificate that is served now, null without TLS and in a push.
// CanGenerate: DDT may issue from its root, which HasRoot says exists; without one, Generate makes a new root.
public sealed record CertificateView(
    bool Manageable,
    string? NotManageable,
    ServerCertificateView? Served,
    DateTimeOffset? ProvisionalUntil,
    bool? ServedHere,
    bool CanGenerate,
    bool HasRoot,
    SettingsSectionView<CertificateSettings> Names);

// Either CertificatePem and KeyPem, the certificate followed by its intermediates, or Pfx in base64 with its password.
// Confirm lists the warning codes the administrator accepted, such as certificate.newRoot.
public sealed record CertificateUpload(string? CertificatePem, string? KeyPem, string? Pfx, string? PfxPassword, IReadOnlyList<string>? Confirm);

public sealed record CertificateGenerate(IReadOnlyList<string>? Confirm);

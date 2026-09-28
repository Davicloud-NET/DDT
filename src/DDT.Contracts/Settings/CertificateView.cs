// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Server;

namespace DDT.Contracts.Settings;

public sealed record CertificateView(
    // False, with the reason in NotManageable, when the page cannot change the certificate.
    bool Manageable,
    string? NotManageable,
    // The certificate every new connection gets.
    ServerCertificateView? Served,
    // Set while a new pair waits to be confirmed. The confirmation has to come from a connection that was served the
    // new pair. Otherwise DDT goes back to the previous pair when this time passes.
    DateTimeOffset? ProvisionalUntil,
    // Whether this request's connection got the certificate that's served now. Null without TLS and in a push.
    bool? ServedHere,
    // Whether DDT may issue a certificate from its root. HasRoot says whether the root exists. Without one, Generate
    // makes a new root.
    bool CanGenerate,
    bool HasRoot,
    SettingsSectionView<CertificateSettings> Names);

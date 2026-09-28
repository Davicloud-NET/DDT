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
    // Set while a new pair waits for its confirmation, which has to come from a connection that was served it, or DDT
    // goes back to the pair before it then.
    DateTimeOffset? ProvisionalUntil,
    // Whether this request's connection was served the certificate served now; null without TLS and in a push.
    bool? ServedHere,
    // DDT may issue from its root, which HasRoot says exists; without one, Generate makes a new root.
    bool CanGenerate,
    bool HasRoot,
    SettingsSectionView<CertificateSettings> Names);

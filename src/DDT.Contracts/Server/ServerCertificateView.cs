// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Server;

public sealed record ServerCertificateView(
    // True when DDT's root issued the certificate and DDT renews it. False for an administrator's certificate, and
    // then the root fields and RenewsUtc are null.
    bool ManagedByDdt,
    string Subject,
    string Sha256,
    DateTimeOffset NotAfter,
    DateTimeOffset? RenewsUtc,
    IReadOnlyList<string> Names,
    string? RootSubject,
    string? RootSha256,
    DateTimeOffset? RootNotAfter,
    // When DDT replaced the self-signed certificate that older boot images pin. It stays set until an administrator
    // confirms that every boot image was rebuilt with the root.
    DateTimeOffset? AnchorReplacedUtc);

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Server;

public sealed record ServerCertificateView(
    // Issued from DDT's root and renewed by DDT; otherwise an administrator's certificate, and the root fields and
    // RenewsUtc are null.
    bool ManagedByDdt,
    string Subject,
    string Sha256,
    DateTimeOffset NotAfter,
    DateTimeOffset? RenewsUtc,
    IReadOnlyList<string> Names,
    string? RootSubject,
    string? RootSha256,
    DateTimeOffset? RootNotAfter,
    // When DDT replaced the self-signed certificate that older boot images pin; set until an administrator confirms
    // every boot image was built again with the root.
    DateTimeOffset? AnchorReplacedUtc);

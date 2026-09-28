// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Images;

// For Trusted and SignedByOthers, Signer is the signing certificate's common name. For the other statuses, Reason is
// the end of a sentence about the file. Anchors are the trusted certificates the valid signatures lead to.
public sealed record AuthenticodeResult(
    AuthenticodeStatus Status,
    ushort? Machine,
    string? Signer,
    string? Reason,
    IReadOnlyList<X509Certificate2>? Anchors = null);

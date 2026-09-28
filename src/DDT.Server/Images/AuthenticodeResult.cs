// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Images;

// Signer, for Trusted and SignedByOthers, is the signing certificate's common name, and Reason, for the others, ends a
// sentence about the file. Anchors are the trusted certificates the valid signatures lead to.
public sealed record AuthenticodeResult(
    AuthenticodeStatus Status,
    ushort? Machine,
    string? Signer,
    string? Reason,
    IReadOnlyList<X509Certificate2>? Anchors = null);

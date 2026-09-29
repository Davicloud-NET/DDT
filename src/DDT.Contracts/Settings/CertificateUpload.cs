// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// Either CertificatePem and KeyPem, or Pfx in base64 with its password. CertificatePem holds the certificate followed
// by its intermediates. Confirm lists the warning codes the administrator accepted, such as certificate.newRoot.
public sealed record CertificateUpload(string? CertificatePem, string? KeyPem, string? Pfx, string? PfxPassword, IReadOnlyList<string>? Confirm);

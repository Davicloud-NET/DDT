// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The certificate section. It holds the names clients use to reach the server. Generate issues a certificate for
// them, and an uploaded one has to cover them.
public sealed record CertificateSettings(IReadOnlyList<string> SubjectAlternativeNames);

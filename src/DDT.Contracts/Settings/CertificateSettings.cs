// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Settings;

// The section certificate: the names the server is reached by, which Generate issues for and an upload has to cover.
public sealed record CertificateSettings(IReadOnlyList<string> SubjectAlternativeNames);

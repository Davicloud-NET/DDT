// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Configuration;

public sealed class HttpsOptions
{
    // The agent pins the DDT certificate chain and validates the hostname, so every name and
    // address the server is reached by has to be in the certificate.
    public string SubjectAlternativeNames { get; init; } = string.Empty;

    public bool GenerateSelfSignedCertificate { get; init; } = true;
}

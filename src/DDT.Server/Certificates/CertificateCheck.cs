// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Certificates;

// Certificate: what is served after the check. ExpiresSoon: an administrator's certificate entered its last 30 days,
// reported once per certificate. Problem: why changed files did not load.
public sealed record CertificateCheck(
    CertificateAction Action,
    X509Certificate2 Certificate,
    bool ManagedByDdt,
    bool ExpiresSoon,
    string? Problem);

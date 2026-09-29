// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Certificates;

// Certificate is what's served after the check. ExpiresSoon is set on the first check after an administrator's
// certificate enters its last 30 days, once per certificate. Problem says why changed files didn't load.
public sealed record CertificateCheck(
    CertificateAction Action,
    X509Certificate2 Certificate,
    bool ManagedByDdt,
    bool ExpiresSoon,
    string? Problem);

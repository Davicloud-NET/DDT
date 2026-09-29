// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Certificates;

public sealed class CertificateRolledBackEventArgs(string rolledBackThumbprint, X509Certificate2 restored) : EventArgs
{
    public string RolledBackThumbprint { get; } = rolledBackThumbprint;

    public X509Certificate2 Restored { get; } = restored;
}

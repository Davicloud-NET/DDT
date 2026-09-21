// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Certificates;

// A certificate and its private key as the two files hold them.
public sealed record PemPair(string CertificatePem, string KeyPem)
{
    // The generated ToString would print the private key into any log or assertion message that shows a pair.
    public override string ToString() => nameof(PemPair);
}

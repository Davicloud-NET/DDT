// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Certificates;

public enum CertificateAction
{
    // The files were there and nothing had to change.
    Unchanged,

    // A new root and a server certificate from it.
    Created,

    // A server certificate from the existing root, because the pair was missing.
    Issued,
}

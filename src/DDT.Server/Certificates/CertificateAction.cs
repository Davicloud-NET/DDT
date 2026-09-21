// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Certificates;

public enum CertificateAction
{
    // The files did not change and nothing was due.
    Unchanged,

    // The first check of this process loaded the files as they were.
    Loaded,

    // The files changed on disk, by another DDT process or an administrator, and were loaded again.
    Reloaded,

    // The changed files do not load, so the certificate loaded before stays in service.
    LoadFailed,

    // A new root and a server certificate from it.
    Created,

    // A server certificate from the existing root, because the pair was missing or did not load.
    Issued,

    // A server certificate from the existing root, because the served one had 30 days or less left.
    Renewed,

    // A server certificate from the existing root, because a configured name was missing from the served one.
    Reissued,
}

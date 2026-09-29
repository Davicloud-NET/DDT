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

    // Another DDT process or an administrator changed the files on disk, and they were loaded again.
    Reloaded,

    // The changed files don't load, so the certificate loaded before stays in service.
    LoadFailed,

    // DDT created a new root and issued a server certificate from it.
    Created,

    // DDT issued a server certificate from the existing root because the pair was missing or DDT's own didn't load.
    Issued,

    // DDT issued a server certificate from the existing root because the served one had 30 days or less left.
    Renewed,

    // DDT issued a server certificate from the existing root because a configured name was missing from the served one.
    Reissued,

    // DDT created a new root and a server certificate from it. They replace the self-signed certificate that a DDT
    // without a root served. Boot images that pin that certificate have to be built again once.
    Migrated,

    // The settings page uploaded or generated a pair. It's served provisionally until it's confirmed.
    Installed,

    // A provisional pair was not confirmed in time, so the pair before it is served again.
    RolledBack,
}

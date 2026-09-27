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

    // A server certificate from the existing root, because the pair was missing or DDT's own did not load.
    Issued,

    // A server certificate from the existing root, because the served one had 30 days or less left.
    Renewed,

    // A server certificate from the existing root, because a configured name was missing from the served one.
    Reissued,

    // A new root and a server certificate from it, in place of the self-signed certificate DDT generated before it had a
    // root. Boot images built before pin that certificate and have to be built again once.
    Migrated,

    // A pair from the settings page, uploaded or generated, served provisionally until it is confirmed.
    Installed,

    // A provisional pair was not confirmed in time, so the pair before it is served again.
    RolledBack,
}

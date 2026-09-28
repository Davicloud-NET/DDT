// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.E2E;

// The published agent, the server it calls with the root it pins, and the dry run it stands in for.
internal sealed record AgentStartInfo(string AgentPath, Uri Server, string RootCertificatePath, int DryRunId, string LogPath)
{
    // The dry run's machine says Secure Boot is on.
    public bool SecureBoot { get; init; }
}

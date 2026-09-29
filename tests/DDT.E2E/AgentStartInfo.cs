// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.E2E;

// The published agent, the server it calls, the root certificate it pins, and the id of the dry run machine it plays.
internal sealed record AgentStartInfo(string AgentPath, Uri Server, string RootCertificatePath, int DryRunId, string LogPath)
{
    // The dry run's machine reports Secure Boot as on.
    public bool SecureBoot { get; init; }
}

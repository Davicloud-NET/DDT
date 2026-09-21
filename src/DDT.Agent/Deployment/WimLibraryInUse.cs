// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The libwim-15.dll the agent loads. Its SHA-256 differs from the carried copy's when someone put their own
// library next to the agent.
public sealed record WimLibraryInUse(string Path, string Sha256, string CarriedSha256)
{
    public bool IsCarriedCopy => string.Equals(Sha256, CarriedSha256, StringComparison.Ordinal);
}

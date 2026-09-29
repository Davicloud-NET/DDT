// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

public sealed record AgentShareConnection(string Path, string UserName, string Password)
{
    public override string ToString() => $"AgentShareConnection {{ Path = {Path}, UserName = {UserName} }}";
}

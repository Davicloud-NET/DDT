// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Deployments;

// Rule: an assignment rule chose the sequence and an operator approved the machine with it on the web.
public enum DeploymentSource
{
    Web,
    Console,
    Rule,
}

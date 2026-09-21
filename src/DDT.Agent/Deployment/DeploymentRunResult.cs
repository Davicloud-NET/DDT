// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;

namespace DDT.Agent.Deployment;

// Token and ResumeToken are the latest the run was given. Step and Percent are where the run ended. UnsentError is
// the failure the server could not be told about, for the loop to report once it can.
public sealed record DeploymentRunResult(
    DeploymentOutcome Outcome,
    string Token,
    string ResumeToken,
    DeploymentStep Step,
    int Percent,
    string? UnsentError);

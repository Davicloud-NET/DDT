// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;

namespace DDT.Server.Deployments;

// A run to create once its request was checked. RuleId is the rule that chose the sequence, DiskNumber the disk chosen
// at the machine.
public sealed record NewRun(RunRequest Request, DeploymentSource Source, GivenAnswers Given, DateTimeOffset Now)
{
    public Guid? RuleId { get; init; }

    public int? DiskNumber { get; init; }

    public bool AllowSecureBootMismatch { get; init; }
}

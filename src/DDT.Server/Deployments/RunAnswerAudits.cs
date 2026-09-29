// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Machines;

namespace DDT.Server.Deployments;

internal static class RunAnswerAudits
{
    // Only the input names, because an answer can be anything a person typed, and an Account input's answer is a
    // password. Null for no answers.
    public static AuditEvent? Of(Deployment run, IReadOnlyList<string> answered, bool atMachine, DateTimeOffset now, Actor actor) =>
        answered.Count == 0
            ? null
            : AuditEvents.Create(
                AuditActions.DeploymentInputsAnswered,
                run.Id.ToString("D"),
                actor,
                now,
                $"{string.Join(", ", answered)} of {run.Title} on machine {run.MachineId:D}, answered {(atMachine ? "at the machine" : "on the web")}.");
}

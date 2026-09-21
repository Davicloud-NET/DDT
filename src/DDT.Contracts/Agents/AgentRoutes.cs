// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

public static class AgentRoutes
{
    public const string Register = "api/agents/register";

    public static string Next(Guid machineId) => $"api/agents/{machineId:D}/next";

    public static string Log(Guid machineId) => $"api/agents/{machineId:D}/log";

    public static string SignIn(Guid machineId) => $"api/agents/{machineId:D}/sign-in";

    public static string Images(Guid machineId) => $"api/agents/{machineId:D}/images";

    public static string ImageContent(Guid machineId, string sha256) => $"api/agents/{machineId:D}/images/{sha256}";

    public static string Deployments(Guid machineId) => $"api/agents/{machineId:D}/deployments";

    public static string DeploymentReport(Guid machineId, Guid deploymentId) =>
        $"api/agents/{machineId:D}/deployments/{deploymentId:D}/report";

    public static string DeploymentUnattend(Guid machineId, Guid deploymentId) =>
        $"api/agents/{machineId:D}/deployments/{deploymentId:D}/unattend";

    // Frozen: agents inside boot images built long ago ask these, so the paths never change.
    public const string Release = "api/agents/release";

    public const string ReleaseBinary = "api/agents/release/binary";
}

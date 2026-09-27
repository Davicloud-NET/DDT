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

    // GET the sequences the technician can pick; POST an AgentRunRequest to Runs to start one.
    public static string Sequences(Guid machineId) => $"api/agents/{machineId:D}/sequences";

    public static string Runs(Guid machineId) => $"api/agents/{machineId:D}/runs";

    public static string RunReport(Guid machineId, Guid runId) => $"api/agents/{machineId:D}/runs/{runId:D}/report";

    // Any image or package of the run, by its hash, with range requests.
    public static string RunFile(Guid machineId, Guid runId, string sha256) =>
        $"api/agents/{machineId:D}/runs/{runId:D}/files/{sha256}";

    // Secrets, served only while the step is the run's Running step.
    public static string RunStepUnattend(Guid machineId, Guid runId, Guid stepId) =>
        $"api/agents/{machineId:D}/runs/{runId:D}/steps/{stepId:D}/unattend";

    public static string RunStepCredentials(Guid machineId, Guid runId, Guid stepId) =>
        $"api/agents/{machineId:D}/runs/{runId:D}/steps/{stepId:D}/credentials";

    // Frozen: agents inside boot images built long ago ask these, so the paths never change.
    public const string Release = "api/agents/release";

    public const string ReleaseBinary = "api/agents/release/binary";

    public const string ConsoleRelease = "api/agents/release/console";

    public static string ConsoleReleaseFile(string name) => $"api/agents/release/console/{Uri.EscapeDataString(name)}";

    public const string ConsoleLogo = "api/agents/console/logo";
}

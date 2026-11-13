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

    // POST an AgentEnvironmentRequest before the machine restarts into the other boot environment.
    public static string Environment(Guid machineId) => $"api/agents/{machineId:D}/environment";

    public static string RunReport(Guid machineId, Guid runId) => $"api/agents/{machineId:D}/runs/{runId:D}/report";

    // POST the AgentInputAnswers given at the machine while the run waits to start.
    public static string RunAnswers(Guid machineId, Guid runId) => $"api/agents/{machineId:D}/runs/{runId:D}/answers";

    // Serves any image or package of the run by its hash, and supports range requests.
    public static string RunFile(Guid machineId, Guid runId, string sha256) =>
        $"api/agents/{machineId:D}/runs/{runId:D}/files/{sha256}";

    // These serve secrets, and only while the step is the run's Running step.
    public static string RunStepUnattend(Guid machineId, Guid runId, Guid stepId) =>
        $"api/agents/{machineId:D}/runs/{runId:D}/steps/{stepId:D}/unattend";

    public static string RunStepCredentials(Guid machineId, Guid runId, Guid stepId) =>
        $"api/agents/{machineId:D}/runs/{runId:D}/steps/{stepId:D}/credentials";

    // GET the AgentFirstLogon as a run in Windows ends, while it still runs there.
    public static string RunFirstLogon(Guid machineId, Guid runId) => $"api/agents/{machineId:D}/runs/{runId:D}/first-logon";

    // GET the step's AgentStepAccounts: its run-as account and its share connections.
    public static string RunStepAccounts(Guid machineId, Guid runId, Guid stepId) =>
        $"api/agents/{machineId:D}/runs/{runId:D}/steps/{stepId:D}/accounts";

    // GET the step's AgentStepApplications while it runs, in Windows. Their files come from RunFile.
    public static string RunStepApplications(Guid machineId, Guid runId, Guid stepId) =>
        $"api/agents/{machineId:D}/runs/{runId:D}/steps/{stepId:D}/applications";

    // Frozen. Agents inside old boot images call these, so the paths never change.
    public const string Release = "api/agents/release";

    public const string ReleaseBinary = "api/agents/release/binary";

    public const string ConsoleRelease = "api/agents/release/console";

    public static string ConsoleReleaseFile(string name) => $"api/agents/release/console/{Uri.EscapeDataString(name)}";

    public const string ConsoleLogo = "api/agents/console/logo";

    // Frozen as well. The agent built for a platform, named as in AgentPlatforms, answered with an AgentRelease too.
    public static string PlatformRelease(string platform) => $"api/agents/releases/{Uri.EscapeDataString(platform)}";

    public static string PlatformReleaseBinary(string platform) => $"api/agents/releases/{Uri.EscapeDataString(platform)}/binary";

    // The console for that platform, answered with a ConsoleRelease, and its files.
    public static string PlatformConsoleRelease(string platform) => $"api/agents/releases/{Uri.EscapeDataString(platform)}/console";

    public static string PlatformConsoleReleaseFile(string platform, string name) =>
        $"api/agents/releases/{Uri.EscapeDataString(platform)}/console/{Uri.EscapeDataString(name)}";
}

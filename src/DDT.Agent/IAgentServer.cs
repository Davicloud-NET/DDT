// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent;

public interface IAgentServer
{
    Task<AgentRegistrationResult> RegisterAsync(AgentRegistration registration, CancellationToken cancellationToken);

    Task<AgentNextResult> NextAsync(Guid machineId, string token, CancellationToken cancellationToken);

    Task SendLogAsync(Guid machineId, string token, AgentLogBatch batch, CancellationToken cancellationToken);

    Task<AgentSignInResult> SignInAsync(Guid machineId, string token, AgentSignInRequest request, CancellationToken cancellationToken);

    // Null when the server offers no agent.
    Task<AgentRelease?> GetReleaseAsync(CancellationToken cancellationToken);

    Task DownloadReleaseAsync(Stream destination, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentImageChoice>> GetImagesAsync(Guid machineId, string token, CancellationToken cancellationToken);

    Task<AgentDeployment> PickImageAsync(Guid machineId, string token, AgentPickRequest request, CancellationToken cancellationToken);

    Task<AgentDeploymentReportResult> ReportDeploymentAsync(
        Guid machineId,
        string token,
        Guid deploymentId,
        AgentDeploymentReport report,
        CancellationToken cancellationToken);

    Task<string> GetUnattendAsync(Guid machineId, string token, Guid deploymentId, CancellationToken cancellationToken);

    // The image's length, or null when the server did not say.
    Task<long?> HeadImageAsync(Guid machineId, string token, string sha256, CancellationToken cancellationToken);

    // From offset to the end. A server that ignores the range answers from 0, which the result's Offset shows.
    Task<AgentImageStream> OpenImageAsync(Guid machineId, string token, string sha256, long offset, CancellationToken cancellationToken);
}

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

    Task<IReadOnlyList<AgentSequenceChoice>> GetSequencesAsync(Guid machineId, string token, CancellationToken cancellationToken);

    // Starts the sequence the technician picked at the machine.
    Task<AgentRun> PickSequenceAsync(Guid machineId, string token, AgentRunRequest request, CancellationToken cancellationToken);

    Task<AgentRunReportResult> ReportRunAsync(Guid machineId, string token, Guid runId, AgentRunReport report, CancellationToken cancellationToken);

    // The length of one of the run's images or packages, or null when the server did not say.
    Task<long?> HeadRunFileAsync(Guid machineId, string token, Guid runId, string sha256, CancellationToken cancellationToken);

    // One of the run's images or packages, from offset to the end. A server that ignores the range answers from 0,
    // which the result's Offset shows.
    Task<AgentImageStream> OpenRunFileAsync(
        Guid machineId,
        string token,
        Guid runId,
        string sha256,
        long offset,
        CancellationToken cancellationToken);

    // The answer file of a WriteUnattend step, which the server renders only while the step is running. It holds
    // passwords.
    Task<string> GetRunUnattendAsync(Guid machineId, string token, Guid runId, Guid stepId, CancellationToken cancellationToken);
}

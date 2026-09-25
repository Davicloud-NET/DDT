// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;

namespace DDT.Server.Tests;

// Runs as the web UI assigns and reads them.
internal static class RunRequests
{
    public static Task<HttpResponseMessage> AssignAsync(
        this SignedInClient client,
        Guid machineId,
        Guid sequenceId,
        string? computerName = null,
        bool allowSecureBootMismatch = false) =>
        client.PostAsync($"/api/machines/{machineId}/deployments", new AssignSequenceRequest(sequenceId, computerName, allowSecureBootMismatch));

    public static async Task<DeploymentSummary> AssignedAsync(this SignedInClient client, Guid machineId, Guid sequenceId, string? computerName = null) =>
        (await RegisteredMachine.ReadAsync<MachineSummary>(await client.AssignAsync(machineId, sequenceId, computerName))).Deployment!;

    public static Task<HttpResponseMessage> EndCurrentAsync(this SignedInClient client, Guid machineId) =>
        client.DeleteAsync($"/api/machines/{machineId}/deployments/current");

    public static async Task<DeploymentView> RunAsync(this SignedInClient client, Guid runId) =>
        await RegisteredMachine.ReadAsync<DeploymentView>(await client.GetAsync($"/api/deployments/{runId}"));

    public static Task<HttpResponseMessage> ApproveAsync(
        this SignedInClient client,
        Guid machineId,
        Guid? expectedSequenceId,
        bool allowSecureBootMismatch = false) =>
        client.PostAsync($"/api/machines/{machineId}/approve", new ApproveMachineRequest(expectedSequenceId, allowSecureBootMismatch));
}

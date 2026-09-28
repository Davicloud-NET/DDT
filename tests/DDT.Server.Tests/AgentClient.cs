// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Server.Tests;

// Speaks to the agent endpoints the way DDT.Agent does: bearer tokens, no cookies. A remote address gives the
// client its own rate limit partitions.
public sealed class AgentClient(HttpClient client, string? remoteAddress = null) : IDisposable
{
    public string? RemoteAddress => remoteAddress;

    // An agent that runs task sequences of the current version.
    public static AgentRegistration Registration(string uuid, string mac, params string[] otherMacs) =>
        new(uuid, mac, [mac, .. otherMacs], "Microsoft Corporation", "Virtual Machine", "0000-0000", "1.0.0")
        {
            SequenceVersion = SequenceDefinition.CurrentVersion,
        };

    public Task<HttpResponseMessage> RegisterAsync(AgentRegistration registration, string? bearer = null) =>
        SendAsync(HttpMethod.Post, AgentRoutes.Register, bearer, JsonContent.Create(registration, options: TestJson.Options));

    public Task<HttpResponseMessage> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null, null);

    public Task<HttpResponseMessage> NextAsync(Guid machineId, string token) =>
        SendAsync(HttpMethod.Get, AgentRoutes.Next(machineId), token, null);

    public Task<HttpResponseMessage> LogAsync(Guid machineId, string token, AgentLogBatch batch) =>
        SendAsync(HttpMethod.Post, AgentRoutes.Log(machineId), token, JsonContent.Create(batch, options: TestJson.Options));

    public Task<HttpResponseMessage> SignInAsync(Guid machineId, string token, AgentSignInRequest request) =>
        SendAsync(HttpMethod.Post, AgentRoutes.SignIn(machineId), token, JsonContent.Create(request, options: TestJson.Options));

    public Task<HttpResponseMessage> GetAsync(string path, string token) => SendAsync(HttpMethod.Get, path, token, null);

    public Task<HttpResponseMessage> SequencesAsync(Guid machineId, string token) =>
        SendAsync(HttpMethod.Get, AgentRoutes.Sequences(machineId), token, null);

    public Task<HttpResponseMessage> PickRunAsync(Guid machineId, string token, AgentRunRequest request) =>
        SendAsync(HttpMethod.Post, AgentRoutes.Runs(machineId), token, JsonContent.Create(request, options: TestJson.Options));

    public Task<HttpResponseMessage> RunReportAsync(Guid machineId, string token, Guid runId, AgentRunReport report) =>
        SendAsync(HttpMethod.Post, AgentRoutes.RunReport(machineId, runId), token, JsonContent.Create(report, options: TestJson.Options));

    public Task<HttpResponseMessage> RunAnswersAsync(Guid machineId, string token, Guid runId, params InputAnswer[] answers) =>
        SendAsync(
            HttpMethod.Post,
            AgentRoutes.RunAnswers(machineId, runId),
            token,
            JsonContent.Create(new AgentInputAnswers(answers), options: TestJson.Options));

    public Task<HttpResponseMessage> RunFileAsync(
        Guid machineId,
        string token,
        Guid runId,
        string sha256,
        HttpMethod? method = null,
        RangeHeaderValue? range = null) =>
        SendAsync(method ?? HttpMethod.Get, AgentRoutes.RunFile(machineId, runId, sha256), token, null, request => request.Headers.Range = range);

    public Task<HttpResponseMessage> RunUnattendAsync(Guid machineId, string token, Guid runId, Guid stepId) =>
        SendAsync(HttpMethod.Get, AgentRoutes.RunStepUnattend(machineId, runId, stepId), token, null);

    public Task<HttpResponseMessage> RunCredentialsAsync(Guid machineId, string token, Guid runId, Guid stepId) =>
        SendAsync(HttpMethod.Get, AgentRoutes.RunStepCredentials(machineId, runId, stepId), token, null);

    public Task<HttpResponseMessage> RunAccountsAsync(Guid machineId, string token, Guid runId, Guid stepId) =>
        SendAsync(HttpMethod.Get, AgentRoutes.RunStepAccounts(machineId, runId, stepId), token, null);

    public void Dispose() => client.Dispose();

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? token,
        HttpContent? content,
        Action<HttpRequestMessage>? configure = null)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative)) { Content = content };

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (remoteAddress is not null)
        {
            request.Headers.Add(TestRemoteAddress.Header, remoteAddress);
        }

        configure?.Invoke(request);

        return await client.SendAsync(request);
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Http.Json;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Server.Tests;

// A registered machine that keeps the tokens a real agent keeps: every next and every report hands out fresh
// ones, and a machine that deploys never polls with its first token again.
public sealed class DeployingMachine : IDisposable
{
    private DeployingMachine(AgentClient agent, AgentRegistration registration, AgentRegistrationResult registered)
    {
        Agent = agent;
        Registration = registration;
        Id = registered.MachineId;
        Token = registered.Token!;
        ResumeToken = registered.ResumeToken!;
    }

    public AgentClient Agent { get; }

    public AgentRegistration Registration { get; }

    public Guid Id { get; }

    public string Token { get; private set; }

    public string ResumeToken { get; private set; }

    // The newest run token a report or a registration handed out, as the agent keeps it on disk.
    public string? RunToken { get; private set; }

    public static AgentDisk Disk(int number, string model = "Msft Virtual Disk", long sizeBytes = 64L * 1024 * 1024 * 1024) =>
        new(number, model, sizeBytes, "SCSI", 0);

    public static async Task<DeployingMachine> RegisterAsync(
        DdtApplication application,
        IReadOnlyList<AgentDisk>? disks = null,
        string? remoteAddress = null)
    {
        ArgumentNullException.ThrowIfNull(application);

        AgentClient agent = new(application.CreateDefaultClient(), remoteAddress ?? TestRemoteAddress.Unique());
        AgentRegistration registration = AgentClient.Registration(
            Guid.NewGuid().ToString("D"),
            "02" + Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 5)) with
        {
            Disks = disks,
        };

        AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(registration));

        return new DeployingMachine(agent, registration, registered);
    }

    // An operator signed in at it: approved, and allowed to choose a sequence at the machine.
    public static async Task<DeployingMachine> SignedInAsync(
        DdtApplication application,
        string userName,
        IReadOnlyList<AgentDisk>? disks = null)
    {
        DeployingMachine machine = await RegisterAsync(application, disks);

        AgentSignInResult signedIn = await RegisteredMachine.ReadAsync<AgentSignInResult>(
            await machine.Agent.SignInAsync(machine.Id, machine.Token, new AgentSignInRequest(userName, DdtApplication.Password, null)));

        Assert.Equal(AgentSignInStatus.Succeeded, signedIn.Status);
        await machine.NextAsync();

        return machine;
    }

    // Approved on the web: nobody signed in at it, so only a web assignment gives it a run.
    public static async Task<DeployingMachine> ApprovedAsync(
        DdtApplication application,
        SignedInClient operatorClient,
        IReadOnlyList<AgentDisk>? disks = null)
    {
        ArgumentNullException.ThrowIfNull(operatorClient);

        DeployingMachine machine = await RegisterAsync(application, disks);

        (await operatorClient.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await machine.NextAsync();

        return machine;
    }

    public async Task<AgentNextResult> NextAsync()
    {
        AgentNextResult next = await RegisteredMachine.ReadAsync<AgentNextResult>(await Agent.NextAsync(Id, Token));

        Token = next.Token;
        ResumeToken = next.ResumeToken;

        return next;
    }

    // A run's report, keeping the tokens it hands out as the agent does.
    public async Task<HttpResponseMessage> ReportAsync(Guid runId, AgentRunReport report)
    {
        HttpResponseMessage response = await Agent.RunReportAsync(Id, Token, runId, report);

        if (response.IsSuccessStatusCode)
        {
            AgentRunReportResult result = (await response.Content.ReadFromJsonAsync<AgentRunReportResult>(TestJson.Options))!;
            Token = result.Token;
            ResumeToken = result.ResumeToken;
            RunToken = result.RunToken ?? RunToken;
        }

        return response;
    }

    public async Task ReportOkAsync(Guid runId, AgentRunReport report)
    {
        HttpResponseMessage response = await ReportAsync(runId, report);

        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    // Without the resume token, as a new agent process does after the machine netbooted again.
    public async Task<AgentRegistrationResult> RegisterAgainAsync(AgentClient? from = null)
    {
        AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await (from ?? Agent).RegisterAsync(Registration));

        if (registered.Token is not null)
        {
            Token = registered.Token;
            ResumeToken = registered.ResumeToken!;
        }

        return registered;
    }

    public void Dispose() => Agent.Dispose();
}

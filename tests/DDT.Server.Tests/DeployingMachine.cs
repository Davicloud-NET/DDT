// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Http.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using Xunit;

namespace DDT.Server.Tests;

// A registered machine that keeps its tokens the way a real agent does.
// Every next and every report hands out fresh ones, and a deploying machine never polls with its first token again.
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

    // The answer to the last report the server accepted.
    public AgentRunReportResult? LastReported { get; private set; }

    public static AgentDisk Disk(int number, string model = "Msft Virtual Disk", long sizeBytes = 64L * 1024 * 1024 * 1024) =>
        new(number, model, sizeBytes, "SCSI", 0);

    public static Task<DeployingMachine> RegisterAsync(
        DdtApplication application,
        IReadOnlyList<AgentDisk>? disks = null,
        bool? secureBootEnabled = null,
        int? sequenceVersion = null,
        UefiCa? trustedUefiCas = null) =>
        RegisterAsync(application, registration => registration with
        {
            Disks = disks,
            SecureBootEnabled = secureBootEnabled,
            TrustedUefiCas = trustedUefiCas,
            SequenceVersion = sequenceVersion ?? registration.SequenceVersion,
        });

    // A machine whose agent registers as change makes it, such as from the Linux boot environment.
    public static async Task<DeployingMachine> RegisterAsync(DdtApplication application, Func<AgentRegistration, AgentRegistration> change)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(change);

        AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());
        AgentRegistration registration = change(AgentClient.Registration(
            Guid.NewGuid().ToString("D"),
            "02" + Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 5)));

        AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(registration));

        return new DeployingMachine(agent, registration, registered);
    }

    // A registration from the Linux boot environment, which starts the sequences that write a raw disk image, with one
    // disk.
    public static Func<AgentRegistration, AgentRegistration> LinuxBoot(bool? secureBootEnabled = null, UefiCa? trustedUefiCas = null) =>
        registration => registration with
        {
            Environment = AgentEnvironment.LinuxBoot,
            Disks = [Disk(0)],
            SecureBootEnabled = secureBootEnabled,
            TrustedUefiCas = trustedUefiCas,
        };

    // A machine an operator signed in at. It's approved and may choose a sequence at the machine.
    public static async Task<DeployingMachine> SignedInAsync(
        DdtApplication application,
        string userName,
        IReadOnlyList<AgentDisk>? disks = null,
        bool? secureBootEnabled = null,
        int? sequenceVersion = null)
    {
        DeployingMachine machine = await RegisterAsync(application, disks, secureBootEnabled: secureBootEnabled, sequenceVersion: sequenceVersion);

        return await machine.SignInAsync(userName);
    }

    // A machine an operator signed in at, whose agent registered as change makes it.
    public static async Task<DeployingMachine> SignedInAsync(DdtApplication application, string userName, Func<AgentRegistration, AgentRegistration> change) =>
        await (await RegisterAsync(application, change)).SignInAsync(userName);

    public async Task<DeployingMachine> SignInAsync(string userName)
    {
        AgentSignInResult signedIn = await RegisteredMachine.ReadAsync<AgentSignInResult>(
            await Agent.SignInAsync(Id, Token, new AgentSignInRequest(userName, DdtApplication.Password, null)));

        Assert.Equal(AgentSignInStatus.Succeeded, signedIn.Status);
        await NextAsync();

        return this;
    }

    // A machine approved on the web. Nobody signed in at it, so only a web assignment gives it a run.
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

    // Sends a run's report and keeps the tokens it hands out, like the agent does.
    public async Task<HttpResponseMessage> ReportAsync(Guid runId, AgentRunReport report)
    {
        HttpResponseMessage response = await Agent.RunReportAsync(Id, Token, runId, report);

        if (response.IsSuccessStatusCode)
        {
            AgentRunReportResult result = (await response.Content.ReadFromJsonAsync<AgentRunReportResult>(TestJson.Options))!;
            LastReported = result;
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

    // Registers again without the resume token, like a new agent process after the machine netbooted again.
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

    // Registers from the boot environment the machine restarted into, with the resume token its agent took along. Without
    // it when carried is false, as after a firmware that lost it.
    public async Task<AgentRegistrationResult> RestartIntoAsync(AgentEnvironment environment, bool carried = true)
    {
        AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await Agent.RegisterAsync(
            Registration with { Environment = environment, ResumeToken = carried ? ResumeToken : null }));

        if (registered.Token is not null)
        {
            Token = registered.Token;
            ResumeToken = registered.ResumeToken!;
        }

        return registered;
    }

    public void Dispose() => Agent.Dispose();
}

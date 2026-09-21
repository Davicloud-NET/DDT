// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Authentication;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Xunit;

namespace DDT.Server.Tests;

public sealed class RateLimitTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    // Sends until the server refuses, so the test does not repeat the configured limits.
    private static async Task<bool> RefusedWithinAsync(int attempts, Func<Task<HttpResponseMessage>> send)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            using HttpResponseMessage response = await send();

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return true;
            }
        }

        return false;
    }

    private SignedInClient Anonymous(string remoteAddress)
    {
        CookieContainer cookies = new();
        HttpClient client = application.CreateDefaultClient(new CookieContainerHandler(cookies));
        client.DefaultRequestHeaders.Add(TestRemoteAddress.Header, remoteAddress);

        return new SignedInClient(client, cookies);
    }

    [Fact]
    public async Task SigningInOnTheWebIsLimitedPerAddress()
    {
        using SignedInClient flooding = Anonymous(TestRemoteAddress.Unique());
        using SignedInClient elsewhere = Anonymous(TestRemoteAddress.Unique());
        LoginRequest wrong = new($"nobody-{Guid.NewGuid():N}", "Not the password 42", null, null);

        Assert.True(await RefusedWithinAsync(100, () => flooding.PostAsync("/api/auth/login", wrong)));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await elsewhere.PostAsync("/api/auth/login", wrong)).StatusCode);
    }

    [Fact]
    public async Task RegisteringIsLimitedPerAddress()
    {
        using AgentClient flooding = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());
        using AgentClient elsewhere = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());
        AgentRegistration invalid = AgentClient.Registration("not a UUID", "020000000001");

        Assert.True(await RefusedWithinAsync(1000, () => flooding.RegisterAsync(invalid)));
        Assert.Equal(HttpStatusCode.BadRequest, (await elsewhere.RegisterAsync(invalid)).StatusCode);
    }

    [Fact]
    public async Task AskingForTheAgentReleaseIsLimitedPerAddress()
    {
        using AgentClient flooding = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());
        using AgentClient elsewhere = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());

        Assert.True(await RefusedWithinAsync(1000, () => flooding.GetAsync(AgentRoutes.Release)));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await elsewhere.GetAsync(AgentRoutes.Release)).StatusCode);
    }

    // Machine ids are shown to every viewer, so requests that name one without its token must not use up its limits.
    [Fact]
    public async Task RequestsWithoutAValidTokenLeaveAMachinesLimitsAlone()
    {
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, await application.AdministratorAsync());
        using AgentClient stranger = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());
        string sha256 = new('0', 64);

        for (int attempt = 0; attempt <= MachineLogLimits.MaxAgentRequestsPerMinute; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.NextAsync(machine.Id, "not a token")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.ImageAsync(machine.Id, "not a token", sha256, HttpMethod.Head)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await machine.Agent.NextAsync(machine.Id, machine.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await machine.Agent.ImageAsync(machine.Id, machine.Token, sha256, HttpMethod.Head)).StatusCode);
    }

    // Anyone gets a valid poll token by registering, so a request counts against the machine that holds the token.
    [Fact]
    public async Task AnotherMachinesTokenLeavesAMachinesLimitsAlone()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        using DeployingMachine waiting = await DeployingMachine.RegisterAsync(application);
        using DeployingMachine approved = await DeployingMachine.ApprovedAsync(application, administrator);
        string sha256 = new('0', 64);

        for (int attempt = 0; attempt <= MachineLogLimits.MaxAgentRequestsPerMinute; attempt++)
        {
            Assert.NotEqual(HttpStatusCode.OK, (await waiting.Agent.NextAsync(machine.Id, waiting.Token)).StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, (await approved.Agent.ImageAsync(machine.Id, approved.Token, sha256, HttpMethod.Head)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await machine.Agent.NextAsync(machine.Id, machine.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await machine.Agent.ImageAsync(machine.Id, machine.Token, sha256, HttpMethod.Head)).StatusCode);
    }
}

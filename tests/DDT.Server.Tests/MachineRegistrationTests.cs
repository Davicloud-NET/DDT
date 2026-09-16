using System.Net;
using System.Net.Http.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

public sealed class MachineRegistrationTests : IClassFixture<DdtApplication>
{
    private readonly DdtApplication _application;

    public MachineRegistrationTests(DdtApplication application)
    {
        _application = application;
    }

    private static string NewUuid() => Guid.NewGuid().ToString("D");

    private static string NewMac() => "02" + Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 5);

    private async Task<(SignedInClient Admin, string Token)> EnrollmentTokenAsync() =>
        (await _application.AdministratorAsync(), await _application.CreateEnrollmentTokenAsync());

    private AgentClient Agent() => new(_application.CreateDefaultClient());

    private static AgentLogBatch OneLine(string message) =>
        new([new AgentLogLine(DateTimeOffset.UtcNow, AgentLogLevel.Information, message)]);

    private static async Task<(Guid MachineId, string SessionToken)> ApprovedMachineAsync(SignedInClient admin, AgentClient agent, string token)
    {
        AgentRegistrationResult registered = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, AgentClient.Registration(NewUuid(), NewMac())));
        (await admin.PostAsync($"/api/machines/{registered.MachineId}/approve")).EnsureSuccessStatusCode();
        AgentNextResult next = await ReadAsync<AgentNextResult>(await agent.NextAsync(registered.MachineId, registered.Token!));

        return (registered.MachineId, next.Token);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");

        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options))!;
    }

    [Fact]
    public async Task RefusesRegistrationWithoutAValidEnrollmentToken()
    {
        using AgentClient agent = Agent();
        AgentRegistration registration = AgentClient.Registration(NewUuid(), NewMac());

        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.RegisterAsync(null, registration)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.RegisterAsync("ddt1.00000000000000000000000000000000.AAAA", registration)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.RegisterAsync("not a token", registration)).StatusCode);
    }

    [Fact]
    public async Task RefusesARevokedEnrollmentToken()
    {
        (SignedInClient admin, string token) = await EnrollmentTokenAsync();
        Guid id = Guid.ParseExact(token.Split('.')[1], "N");

        (await admin.PostAsync($"/api/enrollment-tokens/{id}/revoke")).EnsureSuccessStatusCode();

        using AgentClient agent = Agent();
        HttpResponseMessage response = await agent.RegisterAsync(token, AgentClient.Registration(NewUuid(), NewMac()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegistersAPendingMachineThatCanOnlyPoll()
    {
        (SignedInClient admin, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();
        string uuid = NewUuid();

        AgentRegistrationResult registered = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, AgentClient.Registration(uuid, NewMac())));

        Assert.Equal(MachineState.Pending, registered.State);
        Assert.NotNull(registered.Token);
        Assert.NotNull(registered.ResumeToken);

        AgentNextResult next = await ReadAsync<AgentNextResult>(await agent.NextAsync(registered.MachineId, registered.Token));

        Assert.Equal(MachineState.Pending, next.State);

        // The enrollment token is public, so what it leads to must not write anything but last seen.
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.LogAsync(registered.MachineId, next.Token, OneLine("hello"))).StatusCode);

        IReadOnlyList<MachineSummary> machines = await ReadAsync<IReadOnlyList<MachineSummary>>(await admin.GetAsync("/api/machines"));
        MachineSummary machine = Assert.Single(machines, m => m.Id == registered.MachineId);
        Assert.Equal(uuid, machine.SmbiosUuid);
        Assert.Equal("Virtual Machine", machine.Model);
    }

    [Fact]
    public async Task IssuesASessionTokenOnceApproved()
    {
        (SignedInClient admin, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();

        AgentRegistrationResult registered = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, AgentClient.Registration(NewUuid(), NewMac())));

        MachineSummary approved = await ReadAsync<MachineSummary>(await admin.PostAsync($"/api/machines/{registered.MachineId}/approve"));
        Assert.Equal(MachineState.Approved, approved.State);

        AgentNextResult next = await ReadAsync<AgentNextResult>(await agent.NextAsync(registered.MachineId, registered.Token!));

        Assert.Equal(MachineState.Approved, next.State);
        Assert.Equal(HttpStatusCode.NoContent, (await agent.LogAsync(registered.MachineId, next.Token, OneLine("hello"))).StatusCode);

        AgentNextResult withSession = await ReadAsync<AgentNextResult>(await agent.NextAsync(registered.MachineId, next.Token));
        Assert.Equal(MachineState.Approved, withSession.State);
    }

    [Fact]
    public async Task RejectionKillsEveryTokenAndRegistrationStaysRejected()
    {
        (SignedInClient admin, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();
        AgentRegistration registration = AgentClient.Registration(NewUuid(), NewMac());

        AgentRegistrationResult registered = await ReadAsync<AgentRegistrationResult>(await agent.RegisterAsync(token, registration));

        (await admin.PostAsync($"/api/machines/{registered.MachineId}/reject")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.NextAsync(registered.MachineId, registered.Token!)).StatusCode);

        AgentRegistrationResult again = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, registration with { ResumeToken = registered.ResumeToken }));
        Assert.Equal(MachineState.Rejected, again.State);
        Assert.Null(again.Token);
        Assert.Null(again.ResumeToken);
    }

    [Fact]
    public async Task RegisteringAnApprovedMachineAgainStartsOverAtPending()
    {
        (SignedInClient admin, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();
        AgentRegistration registration = AgentClient.Registration(NewUuid(), NewMac());

        AgentRegistrationResult first = await ReadAsync<AgentRegistrationResult>(await agent.RegisterAsync(token, registration));
        (await admin.PostAsync($"/api/machines/{first.MachineId}/approve")).EnsureSuccessStatusCode();
        AgentNextResult approved = await ReadAsync<AgentNextResult>(await agent.NextAsync(first.MachineId, first.Token!));

        // Anyone holding the enrollment token can present this UUID and MAC, so the approval must not carry over.
        AgentRegistrationResult second = await ReadAsync<AgentRegistrationResult>(await agent.RegisterAsync(token, registration));

        Assert.Equal(first.MachineId, second.MachineId);
        Assert.Equal(MachineState.Pending, second.State);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.NextAsync(first.MachineId, approved.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.NextAsync(first.MachineId, first.Token!)).StatusCode);
    }

    [Fact]
    public async Task RegisteringAPendingMachineAgainInvalidatesItsTokensAndIsAudited()
    {
        (_, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();
        AgentRegistration registration = AgentClient.Registration(NewUuid(), NewMac());

        AgentRegistrationResult first = await ReadAsync<AgentRegistrationResult>(await agent.RegisterAsync(token, registration));
        AgentRegistrationResult second = await ReadAsync<AgentRegistrationResult>(await agent.RegisterAsync(token, registration));

        Assert.Equal(first.MachineId, second.MachineId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.NextAsync(first.MachineId, first.Token!)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.NextAsync(second.MachineId, second.Token!)).StatusCode);

        using IServiceScope scope = _application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        string subject = first.MachineId.ToString("D");

        Assert.Equal(
            [AuditActions.MachineRegistered, AuditActions.MachineReregistered],
            await database.AuditEvents
                .Where(e => e.SubjectId == subject)
                .OrderBy(e => e.Id)
                .Select(e => e.Action)
                .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResumingWithTheResumeTokenKeepsTheApproval()
    {
        (SignedInClient admin, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();
        AgentRegistration registration = AgentClient.Registration(NewUuid(), NewMac());

        AgentRegistrationResult first = await ReadAsync<AgentRegistrationResult>(await agent.RegisterAsync(token, registration));
        (await admin.PostAsync($"/api/machines/{first.MachineId}/approve")).EnsureSuccessStatusCode();
        AgentNextResult approved = await ReadAsync<AgentNextResult>(await agent.NextAsync(first.MachineId, first.Token!));

        AgentRegistrationResult resumed = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, registration with { ResumeToken = approved.ResumeToken }));

        Assert.Equal(first.MachineId, resumed.MachineId);
        Assert.Equal(MachineState.Approved, resumed.State);
        Assert.Equal(HttpStatusCode.OK, (await agent.NextAsync(first.MachineId, approved.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await agent.LogAsync(first.MachineId, resumed.Token!, OneLine("resumed"))).StatusCode);
    }

    [Fact]
    public async Task IgnoresAResumeTokenOfAnotherMachine()
    {
        (SignedInClient admin, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();
        AgentRegistration victim = AgentClient.Registration(NewUuid(), NewMac());

        AgentRegistrationResult approvedMachine = await ReadAsync<AgentRegistrationResult>(await agent.RegisterAsync(token, victim));
        (await admin.PostAsync($"/api/machines/{approvedMachine.MachineId}/approve")).EnsureSuccessStatusCode();

        AgentRegistrationResult other = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, AgentClient.Registration(NewUuid(), NewMac())));

        AgentRegistrationResult hijack = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, victim with { ResumeToken = other.ResumeToken }));

        Assert.Equal(approvedMachine.MachineId, hijack.MachineId);
        Assert.Equal(MachineState.Pending, hijack.State);
    }

    [Fact]
    public async Task NeverMatchesAMachineByMacAloneAcrossUuids()
    {
        (_, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();
        string mac = NewMac();

        AgentRegistrationResult real = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, AgentClient.Registration(NewUuid(), mac)));
        AgentRegistrationResult zeros = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, AgentClient.Registration("00000000-0000-0000-0000-000000000000", mac)));
        AgentRegistrationResult ones = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, AgentClient.Registration("ffffffff-ffff-ffff-ffff-ffffffffffff", mac)));

        Assert.Equal(3, new HashSet<Guid> { real.MachineId, zeros.MachineId, ones.MachineId }.Count);
    }

    [Fact]
    public async Task TellsMachinesWithTheSameUuidApartByTheirAdapters()
    {
        (_, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();
        string uuid = NewUuid();
        string onboard = NewMac();
        string dock = NewMac();

        AgentRegistrationResult first = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, AgentClient.Registration(uuid, onboard, dock)));
        AgentRegistrationResult clone = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, AgentClient.Registration(uuid, NewMac())));
        AgentRegistrationResult throughTheDock = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, AgentClient.Registration(uuid, dock)));

        Assert.NotEqual(first.MachineId, clone.MachineId);
        Assert.Equal(first.MachineId, throughTheDock.MachineId);
    }

    [Fact]
    public async Task RefusesATokenPresentedForAnotherMachine()
    {
        (_, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();

        AgentRegistrationResult one = await ReadAsync<AgentRegistrationResult>(await agent.RegisterAsync(token, AgentClient.Registration(NewUuid(), NewMac())));
        AgentRegistrationResult two = await ReadAsync<AgentRegistrationResult>(await agent.RegisterAsync(token, AgentClient.Registration(NewUuid(), NewMac())));

        Assert.Equal(HttpStatusCode.Forbidden, (await agent.NextAsync(two.MachineId, one.Token!)).StatusCode);
    }

    [Theory]
    [InlineData("not-a-uuid", "02AABBCCDDEE")]
    [InlineData("44454c4c-5700-1038-8036-b7c04f5a344a", "02AABBCCDD")]
    [InlineData("44454c4c-5700-1038-8036-b7c04f5a344a", "02-AA-BB-CC-DD-ZZ")]
    public async Task RefusesMalformedRegistrations(string uuid, string mac)
    {
        (_, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();

        HttpResponseMessage response = await agent.RegisterAsync(token, AgentClient.Registration(uuid, mac));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StoresLogLinesAndCapsTheBatch()
    {
        (SignedInClient admin, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();
        (Guid machineId, string session) = await ApprovedMachineAsync(admin, agent, token);

        AgentLogBatch batch = new(
        [
            new AgentLogLine(DateTimeOffset.UtcNow, AgentLogLevel.Information, "Regis\0tered"),
            new AgentLogLine(DateTimeOffset.UtcNow, AgentLogLevel.Warning, new string('x', 10_000)),
        ]);

        Assert.Equal(HttpStatusCode.NoContent, (await agent.LogAsync(machineId, session, batch)).StatusCode);

        IReadOnlyList<MachineLogEntry> log = await ReadAsync<IReadOnlyList<MachineLogEntry>>(
            await admin.GetAsync($"/api/machines/{machineId}/log"));

        Assert.Equal(["Registered", new string('x', 4000)], log.Select(entry => entry.Message));
        Assert.Equal(AgentLogLevel.Warning, log[1].Level);

        AgentLogBatch tooLarge = new([.. Enumerable.Repeat(new AgentLogLine(DateTimeOffset.UtcNow, AgentLogLevel.Information, "x"), 201)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.LogAsync(machineId, session, tooLarge)).StatusCode);
    }

    [Fact]
    public async Task KeepsOnlyTheNewestLogLinesOfAMachine()
    {
        (SignedInClient admin, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();
        (Guid machineId, string session) = await ApprovedMachineAsync(admin, agent, token);
        const int batches = (MachineLogLimits.MaxStoredLinesPerMachine / MachineLogLimits.MaxLinesPerBatch) + 1;

        for (int batch = 0; batch < batches; batch++)
        {
            AgentLogBatch lines = new(
            [
                .. Enumerable.Range(batch * MachineLogLimits.MaxLinesPerBatch, MachineLogLimits.MaxLinesPerBatch)
                    .Select(line => new AgentLogLine(DateTimeOffset.UtcNow, AgentLogLevel.Information, $"line {line}")),
            ]);

            Assert.Equal(HttpStatusCode.NoContent, (await agent.LogAsync(machineId, session, lines)).StatusCode);
        }

        using IServiceScope scope = _application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        List<string> stored = await database.MachineLogLines
            .Where(l => l.MachineId == machineId)
            .OrderBy(l => l.Id)
            .Select(l => l.Message)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(MachineLogLimits.MaxStoredLinesPerMachine, stored.Count);
        Assert.Equal($"line {MachineLogLimits.MaxLinesPerBatch}", stored[0]);
    }

    [Fact]
    public async Task OnlyOperatorsAndAdministratorsDecide()
    {
        (_, string token) = await EnrollmentTokenAsync();
        using AgentClient agent = Agent();
        using SignedInClient viewer = await _application.SignInAsync(DdtRoleNames.Viewer);
        using SignedInClient operatorClient = await _application.SignInAsync(DdtRoleNames.Operator);

        AgentRegistrationResult registered = await ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(token, AgentClient.Registration(NewUuid(), NewMac())));

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/machines/{registered.MachineId}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync("/api/enrollment-tokens", new CreateEnrollmentTokenRequest("x", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await operatorClient.PostAsync($"/api/machines/{registered.MachineId}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await operatorClient.PostAsync($"/api/machines/{registered.MachineId}/reject")).StatusCode);

        HttpResponseMessage conflict = await operatorClient.PostAsync($"/api/machines/{registered.MachineId}/approve");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("The machine is Rejected.", (await conflict.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))?.Title);
    }

    [Fact]
    public async Task RefusesAnAnonymousHubConnectionWithoutRedirecting()
    {
        using HttpClient anonymous = _application.CreateDefaultClient();

        HttpResponseMessage response = await anonymous.PostAsync(new Uri("/hubs/live/negotiate?negotiateVersion=1", UriKind.Relative), null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Origin", "http://evil.example")]
    [InlineData("Origin", "http://localhost:5302")]
    [InlineData("Sec-Fetch-Site", "same-site")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    public async Task RefusesAHubConnectionFromAnotherOrigin(string header, string value)
    {
        SignedInClient administrator = await _application.AdministratorAsync();

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri("/hubs/live/negotiate?negotiateVersion=1", UriKind.Relative));
        request.Headers.Add(header, value);

        HttpResponseMessage response = await administrator.Http.SendAsync(request, TestContext.Current.CancellationToken);

        // SameSite keeps the cookie off cross site requests only; a same site page on another port or host
        // would otherwise read every live event.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AcceptsAHubConnectionFromTheSameOrigin()
    {
        SignedInClient administrator = await _application.AdministratorAsync();

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri("/hubs/live/negotiate?negotiateVersion=1", UriKind.Relative));
        request.Headers.Add("Origin", "http://localhost");

        HttpResponseMessage response = await administrator.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PushesARegistrationToSignedInViewers()
    {
        (_, string token) = await EnrollmentTokenAsync();
        using SignedInClient viewer = await _application.SignInAsync(DdtRoleNames.Viewer);
        Uri hub = new(_application.Server.BaseAddress, "hubs/live");

        await using HubConnection connection = new HubConnectionBuilder()
            .WithUrl(hub, options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _application.Server.CreateHandler();
                options.Headers["Cookie"] = viewer.Cookies.GetCookieHeader(_application.Server.BaseAddress);
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions = TestJson.Options)
            .Build();

        TaskCompletionSource<MachineSummary> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<MachineSummary>("machineChanged", machine => received.TrySetResult(machine));

        await connection.StartAsync(TestContext.Current.CancellationToken);

        using AgentClient agent = Agent();
        string uuid = NewUuid();
        (await agent.RegisterAsync(token, AgentClient.Registration(uuid, NewMac()))).EnsureSuccessStatusCode();

        MachineSummary pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(uuid, pushed.SmbiosUuid);
        Assert.Equal(MachineState.Pending, pushed.State);
    }
}

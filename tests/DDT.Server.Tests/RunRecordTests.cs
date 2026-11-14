// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Machines;
using DDT.Server.Records;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.RecordingRuns;

namespace DDT.Server.Tests;

// A run's Running step hands over a recovery key or a hardware hash, and only a step of the matching kind can.
public sealed class RunRecordTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static AgentRecoveryKey Key(Guid protectorId, string password = Password, string volume = "C:") => new(protectorId, password, volume);

    private static async Task<string?> RefusedAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);

        return await TestDatabase.TitleAsync(response);
    }

    [Fact]
    public async Task AKeyIsTakenOnlyFromARunningEnableBitLockerStep()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application, "applyImage");
        using DeployingMachine held = machine;
        Guid protectorId = Guid.NewGuid();

        Assert.Equal(
            "Only a running Enable BitLocker step hands over a recovery key.",
            await RefusedAsync(await SendAsync(machine, run.Id, Key(protectorId)), HttpStatusCode.Conflict));

        await SetKindAsync(application, run.Id, run.Sequence.Steps[0].Id, StepKindNames.EnableBitLocker);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(machine, run.Id, Key(protectorId))).StatusCode);

        RecoveryKey key = Assert.Single(await KeysAsync(application, machine.Id));
        Assert.Equal((protectorId, "C:", run.Id, (DateTimeOffset?)null), (key.ProtectorId, key.Volume, key.RunId, key.ReplacedUtc));
        Assert.DoesNotContain(Password, key.ProtectedPassword, StringComparison.Ordinal);
        using IServiceScope scope = application.Services.CreateScope();
        Assert.Equal(Password, scope.ServiceProvider.GetRequiredService<RecoveryKeyProtector>().Unprotect(machine.Id, protectorId, key.ProtectedPassword));
    }

    // The purpose names machine and protector, so a password copied to another key's row is unreadable there.
    [Fact]
    public void AProtectedPasswordOnlyOpensForItsMachineAndProtector()
    {
        using IServiceScope scope = application.Services.CreateScope();
        RecoveryKeyProtector protector = scope.ServiceProvider.GetRequiredService<RecoveryKeyProtector>();
        (Guid machineId, Guid protectorId) = (Guid.NewGuid(), Guid.NewGuid());
        string protectedPassword = protector.Protect(machineId, protectorId, Password);

        Assert.Equal(Password, protector.Unprotect(machineId, protectorId, protectedPassword));
        Assert.Null(protector.Unprotect(Guid.NewGuid(), protectorId, protectedPassword));
        Assert.Null(protector.Unprotect(machineId, Guid.NewGuid(), protectedPassword));
    }

    [Fact]
    public async Task TheSameKeyAgainAnswersAsTheFirstTime()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application, StepKindNames.EnableBitLocker);
        using DeployingMachine held = machine;
        Guid protectorId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(machine, run.Id, Key(protectorId))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(machine, run.Id, Key(protectorId, volume: "c:"))).StatusCode);

        Assert.Single(await KeysAsync(application, machine.Id));
        string subject = machine.Id.ToString("D");
        List<string?> recorded = await application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject && e.Action == AuditActions.RecoveryKeyRecorded)
            .Select(e => e.Detail)
            .ToListAsync(Cancellation));
        Assert.Equal([$"Key ID {protectorId:D} of C:, from run {run.Id:D}."], recorded);

        Assert.Equal(
            $"The server holds another recovery password or volume for key ID {protectorId:D}.",
            await RefusedAsync(await SendAsync(machine, run.Id, Key(protectorId, OtherPassword)), HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task ALaterKeyReplacesTheKeyOfItsVolumeOnly()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application, StepKindNames.EnableBitLocker);
        using DeployingMachine held = machine;
        (Guid first, Guid data, Guid second) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await SendAsync(machine, run.Id, Key(first));
        await SendAsync(machine, run.Id, Key(data, OtherPassword, "D:"));
        await SendAsync(machine, run.Id, Key(second, OtherPassword));

        Dictionary<Guid, DateTimeOffset?> replaced = (await KeysAsync(application, machine.Id)).ToDictionary(k => k.ProtectorId, k => k.ReplacedUtc);
        Assert.NotNull(replaced[first]);
        Assert.Null(replaced[data]);
        Assert.Null(replaced[second]);
    }

    // The title never repeats what was sent.
    [Theory]
    [InlineData("123456-234564-345675-456786-567897-678909-111111-222222")]
    [InlineData("123453-234564-345675-456786-567897-678909-111111")]
    [InlineData("123453 234564 345675 456786 567897 678909 111111 222222")]
    [InlineData("-23453-234564-345675-456786-567897-678909-111111-222222")]
    [InlineData("720896-234564-345675-456786-567897-678909-111111-222222")]
    public async Task AMalformedPasswordIsRefusedWithoutBeingNamed(string password)
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application, StepKindNames.EnableBitLocker);
        using DeployingMachine held = machine;

        string? title = await RefusedAsync(await SendAsync(machine, run.Id, Key(Guid.NewGuid(), password)), HttpStatusCode.BadRequest);

        Assert.Equal("The recovery password is not 8 groups of 6 digits as BitLocker makes them.", title);
        Assert.Empty(await KeysAsync(application, machine.Id));
    }

    [Fact]
    public async Task AVolumeThatIsNoDriveIsRefused()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application, StepKindNames.EnableBitLocker);
        using DeployingMachine held = machine;

        Assert.Equal(
            "The volume is not a drive such as C:.",
            await RefusedAsync(await SendAsync(machine, run.Id, Key(Guid.NewGuid(), volume: @"\\?\Volume{1}")), HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task OnlyTheMachinesOwnRunningRunTakesRecords()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application, StepKindNames.EnableBitLocker);
        using DeployingMachine held = machine;
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine other = await DeployingMachine.ApprovedAsync(application, administrator);
        string json = Json(Key(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, (await other.Agent.RunRecordAsync(machine.Id, other.Token, run.Id, json)).StatusCode);
        Assert.Equal(
            "This machine has no such run that is running. Report the run as running first.",
            await RefusedAsync(await other.Agent.RunRecordAsync(other.Id, other.Token, run.Id, json), HttpStatusCode.Conflict));
        Assert.Equal(HttpStatusCode.Conflict, (await machine.Agent.RunRecordAsync(machine.Id, machine.Token, Guid.NewGuid(), json)).StatusCode);
        Assert.Empty(await KeysAsync(application, machine.Id));
    }

    // Kestrel enforces the limit. The test server can't, so the endpoint's metadata is read instead.
    [Fact]
    public void TheRouteTakesAtMost32Kilobytes()
    {
        EndpointDataSource endpoints = application.Services.GetRequiredService<EndpointDataSource>();

        RouteEndpoint route = Assert.Single(
            endpoints.Endpoints.OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText == "/api/agents/{id:guid}/runs/{runId:guid}/records");

        Assert.Equal(32 * 1024, route.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
    }

    [Fact]
    public async Task AHashOverItsLengthIsRefused()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application, StepKindNames.RegisterAutopilot);
        using DeployingMachine held = machine;

        Assert.Equal(
            "The hardware hash is not base64 of at most 8000 characters.",
            await RefusedAsync(await SendAsync(machine, run.Id, new AgentHardwareHash("SN-1", new string('A', 8004), null)), HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task AnUnknownKindIsRefused()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application, StepKindNames.EnableBitLocker);
        using DeployingMachine held = machine;

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await machine.Agent.RunRecordAsync(machine.Id, machine.Token, run.Id, """{"kind":"wifiKey","password":"x"}""")).StatusCode);
    }

    [Fact]
    public async Task AHardwareHashIsTakenAndItsStateRead()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application, StepKindNames.RegisterAutopilot);
        using DeployingMachine held = machine;
        AgentHardwareHash hash = new(" SN-4711 ", Convert.ToBase64String(new byte[3000]), "Kiosk");

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(machine, run.Id, hash)).StatusCode);
        await application.QueryAsync(database => database.MachineHardwareHashes
            .Where(h => h.MachineId == machine.Id)
            .ExecuteUpdateAsync(h => h.SetProperty(x => x.UploadState, HardwareHashUploadState.Failed).SetProperty(x => x.UploadMessage, "Already assigned"), Cancellation));

        HttpResponseMessage status = await machine.Agent.RunHardwareHashAsync(machine.Id, machine.Token, run.Id);
        Assert.Equal(
            new AgentHardwareHashStatus(HardwareHashUploadState.Failed, "Already assigned"),
            await status.Content.ReadFromJsonAsync<AgentHardwareHashStatus>(TestJson.Options, Cancellation));

        // The same collection again keeps its upload. Another one starts over.
        await SendAsync(machine, run.Id, hash);
        Assert.Equal(HardwareHashUploadState.Failed, (await RowAsync(machine.Id)).UploadState);
        await SendAsync(machine, run.Id, hash with { Hash = Convert.ToBase64String(new byte[3001]) });
        MachineHardwareHash row = await RowAsync(machine.Id);
        Assert.Equal(("SN-4711", "Kiosk", HardwareHashUploadState.None, (string?)null), (row.SerialNumber, row.GroupTag, row.UploadState, row.UploadMessage));
    }

    [Fact]
    public async Task AHashIsTakenOnlyFromARunningRegisterWithAutopilotStep()
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application, StepKindNames.EnableBitLocker);
        using DeployingMachine held = machine;

        Assert.Equal(
            "Only a running Register with Autopilot step hands over a hardware hash or asks about it.",
            await RefusedAsync(await SendAsync(machine, run.Id, new AgentHardwareHash("SN", "AAAA", null)), HttpStatusCode.Conflict));
        Assert.Equal(HttpStatusCode.Conflict, (await machine.Agent.RunHardwareHashAsync(machine.Id, machine.Token, run.Id)).StatusCode);
    }

    [Theory]
    [InlineData("", "AAAA")]
    [InlineData("SN", "not base64!")]
    [InlineData("SN", "")]
    public async Task AMalformedHashIsRefused(string serialNumber, string hash)
    {
        (DeployingMachine machine, AgentRun run) = await RunningAsync(application, StepKindNames.RegisterAutopilot);
        using DeployingMachine held = machine;

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(machine, run.Id, new AgentHardwareHash(serialNumber, hash, null))).StatusCode);
    }

    private Task<MachineHardwareHash> RowAsync(Guid machineId) =>
        application.QueryAsync(database => database.MachineHardwareHashes.AsNoTracking().SingleAsync(h => h.MachineId == machineId, Cancellation));
}

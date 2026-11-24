// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using System.Text.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// Runs whose first step runs and has the kind a test names. The kinds that hand records over have no contract yet, so
// the step's row is given the kind.
internal static class RecordingRuns
{
    // Each group is 11 times a 16-bit number, as BitLocker makes them.
    public const string Password = "123453-234564-345675-456786-567897-678909-111111-222222";

    public const string OtherPassword = "000011-000022-000033-000044-000055-000066-000077-000088";

    public static async Task<(DeployingMachine Machine, AgentRun Run)> RunningAsync(DdtApplication application, string kind)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Guid imageId = (await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id;
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(imageId));
        await administrator.AssignedAsync(machine.Id, sequence.Id);
        AgentRun run = (await machine.NextAsync()).Run!;

        await machine.ReportOkAsync(run.Id, Running(Step(run.Sequence.Steps[0], StepState.Running)));
        await SetKindAsync(application, run.Id, run.Sequence.Steps[0].Id, kind);

        return (machine, run);
    }

    public static Task SetKindAsync(DdtApplication application, Guid runId, Guid stepId, string kind) =>
        application.QueryAsync(database => database.DeploymentSteps
            .Where(s => s.DeploymentId == runId && s.StepId == stepId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Kind, kind), TestContext.Current.CancellationToken));

    public static string Json(AgentRecord record) => JsonSerializer.Serialize(record, AgentJsonContext.Default.AgentRecord);

    public static Task<HttpResponseMessage> SendAsync(DeployingMachine machine, Guid runId, AgentRecord record) =>
        machine.Agent.RunRecordAsync(machine.Id, machine.Token, runId, Json(record));

    public static Task<List<RecoveryKey>> KeysAsync(DdtApplication application, Guid machineId) =>
        application.QueryAsync(database => database.RecoveryKeys
            .AsNoTracking()
            .Where(k => k.MachineId == machineId)
            .ToListAsync(TestContext.Current.CancellationToken));

    // Keeps a key as a run would have, for a machine that never ran.
    public static async Task<RecoveryKey> SeedKeyAsync(
        DdtApplication application,
        Guid machineId,
        string volume = "C:",
        DateTimeOffset? replacedUtc = null)
    {
        using IServiceScope scope = application.Services.CreateScope();
        RecoveryKeyProtector protector = scope.ServiceProvider.GetRequiredService<RecoveryKeyProtector>();
        Guid protectorId = Guid.NewGuid();
        RecoveryKey key = new()
        {
            Id = Guid.CreateVersion7(),
            MachineId = machineId,
            RunId = Guid.NewGuid(),
            ProtectorId = protectorId,
            Volume = volume,
            ProtectedPassword = protector.Protect(machineId, protectorId, Password),
            CreatedUtc = DateTimeOffset.UtcNow,
            ReplacedUtc = replacedUtc,
        };

        await application.QueryAsync(async database =>
        {
            database.RecoveryKeys.Add(key);

            return await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        return key;
    }
}

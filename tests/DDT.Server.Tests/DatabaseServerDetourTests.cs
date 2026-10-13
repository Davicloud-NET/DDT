// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The count of a machine's starts from the network is checked where it is saved, and the id of the start it counted
// last is a column of the database's own type. Only a database server shows that its migration and its check hold.
public sealed class DatabaseServerDetourTests
{
    [Theory]
    [InlineData(DatabaseProvider.PostgreSql)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task CountsAStartOnceAndSavesOnlyOneOfTwoQuestionsThatReadTheSameCount(DatabaseProvider provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TestDatabaseServer server = await TestDatabaseServer.StartAsync(provider);
        using DatabaseServerApplication application = new(server);
        (DeployingMachine machine, AgentRun run) = await HandedOverLinuxRun.StartAsync(application, "LINUX-90");
        using DeployingMachine owned = machine;
        await HandedOverLinuxRun.InstalledAsync(machine);
        Guid start = Guid.NewGuid();

        Assert.Equal(1, (await HandedOverLinuxRun.AskAsync(machine, run.Id, start)).InstalledRun?.Detours);
        Assert.Equal(1, (await HandedOverLinuxRun.AskAsync(machine, run.Id, start)).InstalledRun?.Detours);

        Deployment stored = await application.QueryAsync(database => database.Deployments.AsNoTracking().SingleAsync(d => d.Id == run.Id, cancellationToken));
        Assert.Equal((1, start), (stored.Detours, stored.DetourStart));

        // Two questions that both read the count before either saves it.
        DateTimeOffset now = application.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        using IServiceScope first = application.Services.CreateScope();
        using IServiceScope second = application.Services.CreateScope();
        await first.ServiceProvider.GetRequiredService<MachineArrivals>().RecordAsync(Question(machine, run.Id), IPAddress.Loopback, now, cancellationToken);
        await second.ServiceProvider.GetRequiredService<MachineArrivals>().RecordAsync(Question(machine, run.Id), IPAddress.Loopback, now, cancellationToken);

        await first.ServiceProvider.GetRequiredService<DdtDbContext>().SaveChangesAsync(cancellationToken);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.ServiceProvider.GetRequiredService<DdtDbContext>().SaveChangesAsync(cancellationToken));

        Assert.Equal(2, await application.QueryAsync(database => database.Deployments.Where(d => d.Id == run.Id).Select(d => d.Detours).SingleAsync(cancellationToken)));
        Assert.Equal(
            2,
            await application.QueryAsync(database => database.MachineLogLines.CountAsync(
                line => line.DeploymentId == run.Id && line.Message.StartsWith("The machine started from the network"),
                cancellationToken)));
    }

    private static NormalisedRegistration Question(DeployingMachine machine, Guid runId)
    {
        Assert.True(RegistrationValidator.TryNormalise(
            machine.Registration with { HandedOver = new AgentHandedOverRun(runId, Guid.NewGuid()) },
            out NormalisedRegistration? question,
            out _));

        return Assert.IsType<NormalisedRegistration>(question);
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Contracts.Tokens;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// What a user leaves behind names them: who approved a machine, who last changed a sequence. PostgreSQL clears those
// when the account goes. SQL Server can't, so there DDT does.
public sealed class DatabaseServerUserTests
{
    [Theory]
    [InlineData(DatabaseProvider.PostgreSql)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task DeletingAUserKeepsWhatTheyDidAndForgetsWhoDidIt(DatabaseProvider provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TestDatabaseServer server = await TestDatabaseServer.StartAsync(provider);
        using DatabaseServerApplication application = new(server);
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient leaving = await application.SignInAsync(DdtRoleNames.Administrator);
        Guid leavingId = await application.QueryAsync(async database =>
            (await database.Users.OrderByDescending(user => user.CreatedUtc).FirstAsync(cancellationToken)).Id);

        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, leaving);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceView sequence = await leaving.CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));
        (await leaving.PostAsync("/api/tokens", new CreateApiTokenRequest("Theirs", DdtRoleNames.Viewer, 30))).EnsureSuccessStatusCode();

        Assert.Equal(leavingId, (await application.MachineAsync(machine.Id)).ApprovedByUserId);

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"/api/users/{leavingId}")).StatusCode);

        Machine kept = await application.MachineAsync(machine.Id);
        Assert.Equal(MachineState.Approved, kept.State);
        Assert.Null(kept.ApprovedByUserId);
        Assert.Null(await application.QueryAsync(database =>
            database.TaskSequences.Where(stored => stored.Id == sequence.Id).Select(stored => stored.UpdatedByUserId).SingleAsync(cancellationToken)));
        Assert.Equal(0, await application.QueryAsync(database => database.ApiTokens.CountAsync(token => token.UserId == leavingId, cancellationToken)));
        Assert.False(await application.QueryAsync(database => database.Users.AnyAsync(user => user.Id == leavingId, cancellationToken)));
    }

    // SQL Server's own collations would take these for the same text.
    [Theory]
    [InlineData(DatabaseProvider.PostgreSql)]
    [InlineData(DatabaseProvider.SqlServer)]
    public async Task TextIsComparedExactly(DatabaseProvider provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TestDatabaseServer server = await TestDatabaseServer.StartAsync(provider);
        using DatabaseServerApplication application = new(server);
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, await application.AdministratorAsync());

        string hash = (await application.MachineAsync(machine.Id)).SmbiosUuid;
        string other = hash.ToUpperInvariant() == hash ? hash.ToLowerInvariant() : hash.ToUpperInvariant();

        Assert.NotEqual(hash, other);
        Assert.Equal(1, await application.QueryAsync(database => database.Machines.CountAsync(stored => stored.SmbiosUuid == hash, cancellationToken)));
        Assert.Equal(0, await application.QueryAsync(database => database.Machines.CountAsync(stored => stored.SmbiosUuid == other, cancellationToken)));
    }
}

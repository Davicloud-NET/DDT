// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Threading.Channels;
using DDT.Contracts.Audit;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Packages;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// Other tests in the class write their own audit rows.
// So each test finds its rows by an action or a subject that only it uses.
public sealed class AuditLogTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string Audit = "/api/audit";

    private static readonly DateTimeOffset s_start = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private async Task<AuditPage> PageAsync(string queryString) =>
        await RegisteredMachine.ReadAsync<AuditPage>(await (await application.AdministratorAsync()).GetAsync($"{Audit}?{queryString}"));

    private static string UniqueAction() => $"test.{Guid.NewGuid():N}"[..20];

    private async Task<List<AuditEvent>> SeedAsync(params AuditEvent[] events)
    {
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        database.AuditEvents.AddRange(events);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        return [.. events];
    }

    private static AuditEvent Event(string action, int minute = 0, string? actorName = null, string? subject = null) => new()
    {
        OccurredUtc = s_start.AddMinutes(minute),
        Action = action,
        ActorName = actorName,
        SubjectId = subject,
        SourceAddress = "192.0.2.10",
        Detail = $"Seeded at minute {minute}.",
    };

    [Fact]
    public async Task ListsWhatUsersAndMachinesDidNewestFirst()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();

        AuditPage page = await PageAsync($"subject={machine.Id:D}");

        Assert.Equal([AuditActions.MachineApproved, AuditActions.MachineRegistered], page.Items.Select(e => e.Action));
        Assert.Null(page.Next);

        AuditEntry approved = page.Items[0];
        Assert.Equal(AuditActorKind.User, approved.ActorKind);
        Assert.StartsWith("administrator-", approved.ActorName, StringComparison.Ordinal);
        Assert.NotNull(approved.ActorUserId);
        Assert.Null(approved.ActorMachineId);
        Assert.Equal(machine.Id.ToString("D"), approved.SubjectId);
        Assert.Equal("Was Pending.", approved.Detail);
        Assert.True(approved.Id > page.Items[1].Id);

        AuditEntry registered = page.Items[1];
        Assert.Equal(AuditActorKind.Machine, registered.ActorKind);
        Assert.Equal(machine.Id, registered.ActorMachineId);
        Assert.Null(registered.ActorUserId);
        Assert.NotNull(registered.SourceAddress);
    }

    [Fact]
    public async Task SaysWhenDdtItselfActed()
    {
        string action = UniqueAction();
        await SeedAsync(Event(action));

        Assert.Equal(AuditActorKind.System, Assert.Single((await PageAsync($"action={action}")).Items).ActorKind);
    }

    [Fact]
    public async Task PagesBackWithTheIdOfTheLastRow()
    {
        string action = UniqueAction();
        List<AuditEvent> seeded = await SeedAsync([.. Enumerable.Range(0, 5).Select(minute => Event(action, minute))]);

        AuditPage first = await PageAsync($"action={action}&limit=2");
        AuditPage second = await PageAsync($"action={action}&limit=2&before={first.Next}");
        AuditPage third = await PageAsync($"action={action}&limit=2&before={second.Next}");

        Assert.Equal(first.Items[^1].Id, first.Next);
        Assert.Null(third.Next);
        Assert.Equal(
            seeded.Select(e => e.Id).OrderDescending(),
            first.Items.Concat(second.Items).Concat(third.Items).Select(e => e.Id));
    }

    [Fact]
    public async Task FiltersByTheStartOfTheAction()
    {
        string action = UniqueAction();
        await SeedAsync(Event($"{action}.one"), Event($"{action}.two"), Event($"{action}x.three"));

        Assert.Equal(2, (await PageAsync($"action={action}.")).Items.Count);
        Assert.Single((await PageAsync($"action={action}.two")).Items);
        Assert.Equal(3, (await PageAsync($"action={action.ToUpperInvariant()}")).Items.Count);
    }

    [Fact]
    public async Task FiltersByAnyPartOfTheActorsNameAndByTheExactSubject()
    {
        string action = UniqueAction();
        string subject = Guid.NewGuid().ToString("D");
        await SeedAsync(
            Event(action, actorName: "Alice Example", subject: subject),
            Event(action, actorName: "bob", subject: subject + "0"),
            Event(action, actorName: null, subject: subject));

        Assert.Equal("Alice Example", Assert.Single((await PageAsync($"action={action}&actor=CE%20EX")).Items).ActorName);
        Assert.Equal(2, (await PageAsync($"action={action}&subject={subject}")).Items.Count);
    }

    [Fact]
    public async Task FiltersByWhenWithTheStartInAndTheEndOut()
    {
        string action = UniqueAction();
        await SeedAsync([.. Enumerable.Range(0, 4).Select(minute => Event(action, minute))]);
        string from = Uri.EscapeDataString(s_start.AddMinutes(1).ToString("O"));
        string to = Uri.EscapeDataString(s_start.AddMinutes(3).ToString("O"));

        AuditPage page = await PageAsync($"action={action}&from={from}&to={to}");

        Assert.Equal([s_start.AddMinutes(2), s_start.AddMinutes(1)], page.Items.Select(e => e.OccurredUtc));

        HttpResponseMessage backwards = await (await application.AdministratorAsync()).GetAsync($"{Audit}?from={to}&to={from}");
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
    }

    [Fact]
    public async Task OnlyAdministratorsReadTheLog()
    {
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);

        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync(Audit)).StatusCode);
    }

    // The rows of a change reach the audit page the moment they are stored, and only on administrators' connections.
    [Fact]
    public async Task PushesTheRowsASaveAddedToAdministratorsOnly()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);
        await using LiveListener administratorLive = await LiveListener.StartAsync(application, administrator);
        await using LiveListener operatorLive = await LiveListener.StartAsync(application, operatorClient);
        ChannelReader<AuditEntry[]> pushed = administratorLive.Listen<AuditEntry[]>(LiveEvents.AuditAppended);
        ChannelReader<AuditEntry[]> overheard = operatorLive.Listen<AuditEntry[]>(LiveEvents.AuditAppended);
        ChannelReader<MachineSummary> operatorMachines = operatorLive.Listen<MachineSummary>(LiveEvents.MachineChanged);
        PackageSummary package = await administrator.UploadedPackageAsync(PackageRequests.DriverZip(), UploadKind.Drivers);

        AuditEntry uploaded = Assert.Single(await LiveListener.NextAsync(pushed, rows => rows.Any(r => r.SubjectId == package.Id.ToString("D"))));
        Assert.Equal(AuditActions.PackageUploaded, uploaded.Action);
        Assert.Equal(AuditActorKind.User, uploaded.ActorKind);
        Assert.Equal(uploaded, Assert.Single((await PageAsync($"subject={package.Id:D}")).Items));

        // Waits for something the operator does hear, which is pushed after the upload's rows.
        // So the rows would have arrived by then.
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application);
        await LiveListener.NextAsync(operatorMachines, m => m.Id == machine.Id);
        await LiveListener.NextAsync(pushed, rows => rows.Any(r => r.SubjectId == machine.Id.ToString("D")));

        Assert.False(overheard.TryRead(out _));
    }

    // A save in a transaction that rolls back stores nothing, so nothing is pushed.
    // A save in one that commits is pushed with the commit.
    [Fact]
    public async Task PushesTheRowsOfATransactionWhenItCommits()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener live = await LiveListener.StartAsync(application, administrator);
        ChannelReader<AuditEntry[]> pushed = live.Listen<AuditEntry[]>(LiveEvents.AuditAppended);
        string action = UniqueAction();

        using (IServiceScope scope = application.Services.CreateScope())
        {
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

            await using (IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
            {
                database.AuditEvents.Add(Event($"{action}.rolled-back"));
                await database.SaveChangesAsync(TestContext.Current.CancellationToken);
                await transaction.RollbackAsync(TestContext.Current.CancellationToken);
            }

            database.ChangeTracker.Clear();

            await using (IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
            {
                database.AuditEvents.Add(Event($"{action}.committed"));
                await database.SaveChangesAsync(TestContext.Current.CancellationToken);

                await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
                Assert.False(pushed.TryPeek(out _));

                await transaction.CommitAsync(TestContext.Current.CancellationToken);
            }
        }

        AuditEntry[] rows = await LiveListener.NextAsync(pushed, rows => rows.Any(r => r.Action.StartsWith(action, StringComparison.Ordinal)));

        Assert.Equal($"{action}.committed", Assert.Single(rows).Action);
        Assert.Single((await PageAsync($"action={action}")).Items);
    }
}

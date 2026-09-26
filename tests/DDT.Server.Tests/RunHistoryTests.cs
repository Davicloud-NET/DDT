// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Threading.Channels;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Live;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The run history lists the runs of every machine. Other tests in the class add runs of their own, so each test finds
// its runs by something only they have, such as a model name.
public sealed class RunHistoryTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string History = "/api/deployments";

    private static readonly DateTimeOffset s_start = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private async Task<RunHistoryPage> PageAsync(string queryString, SignedInClient? client = null) =>
        await RegisteredMachine.ReadAsync<RunHistoryPage>(await (client ?? await application.AdministratorAsync()).GetAsync($"{History}?{queryString}"));

    // A registered machine with the details the history shows, changed as an operator or the firmware would set them.
    private async Task<Guid> MachineAsync(string model, string? name = null, string? serial = null, int? chassisType = null)
    {
        using RegisteredMachine machine = await application.RegisterMachineAsync();

        await application.ChangeMachineAsync(machine.Id, m =>
        {
            m.Manufacturer = "Contoso";
            m.Model = model;
            m.AssignedName = name;
            m.SerialNumber = serial;
            m.ChassisType = chassisType;
        });

        return machine.Id;
    }

    // Stored directly, with the creation time a test needs. Ids are minted from it, as the server mints them.
    private async Task<Guid> RunAsync(Guid machineId, DeploymentState state, int minute, string title = "A run", Guid? sequenceId = null, Guid? id = null)
    {
        DateTimeOffset created = s_start.AddMinutes(minute);
        Deployment run = new()
        {
            Id = id ?? Guid.CreateVersion7(created),
            MachineId = machineId,
            TaskSequenceId = sequenceId,
            Title = title,
            State = state,
            Source = DeploymentSource.Web,
            CreatedUtc = created,
            UpdatedUtc = created,
            Error = state == DeploymentState.Failed ? "It broke." : null,
        };

        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        database.Deployments.Add(run);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        return run.Id;
    }

    [Fact]
    public async Task ListsTheRunsOfEveryMachineNewestFirstWithTheirMachine()
    {
        string model = RuleRequests.UniqueModel();
        Guid laptop = await MachineAsync(model, name: "PC-LAPTOP", serial: "SN-1", chassisType: 10);
        Guid desktop = await MachineAsync(model, chassisType: 3);
        Guid oldest = await RunAsync(laptop, DeploymentState.Done, 1, "Install Windows");
        Guid middle = await RunAsync(desktop, DeploymentState.Failed, 2, "Install Windows");
        Guid newest = await RunAsync(laptop, DeploymentState.Running, 3, "Install Office");

        RunHistoryPage page = await PageAsync($"query={Uri.EscapeDataString(model)}");

        Assert.Equal([newest, middle, oldest], page.Items.Select(i => i.Run.Id));
        Assert.Null(page.Next);

        RunHistoryItem first = page.Items[0];
        Assert.Equal(laptop, first.MachineId);
        Assert.Equal("PC-LAPTOP", first.MachineName);
        Assert.Equal(model, first.MachineModel);
        Assert.Equal("Contoso", first.Manufacturer);
        Assert.Equal((await application.MachineAsync(laptop)).PrimaryMac, first.PrimaryMac);
        Assert.Equal(DeviceKind.Laptop, first.DeviceKind);
        Assert.Equal("Install Office", first.Run.Title);
        Assert.Equal(DeploymentState.Running, first.Run.State);
        Assert.Equal(s_start.AddMinutes(3), first.Run.CreatedUtc);

        RunHistoryItem failed = page.Items[1];
        Assert.Null(failed.MachineName);
        Assert.Equal(DeviceKind.Desktop, failed.DeviceKind);
        Assert.Equal("It broke.", failed.Run.Error);

        Assert.Equal(new RunStateCounts(0, 1, 1, 1, 0), page.Counts);
    }

    // Each page continues after the last run of the one before, whatever was added in between, and runs created in the
    // same millisecond each come once.
    [Fact]
    public async Task PagesThroughEveryRunOnceWithACursor()
    {
        string model = RuleRequests.UniqueModel();
        Guid machine = await MachineAsync(model);
        List<Guid> runs = [];

        for (int minute = 0; minute < 4; minute++)
        {
            runs.Add(await RunAsync(machine, DeploymentState.Done, minute));
        }

        runs.Add(await RunAsync(machine, DeploymentState.Done, 3));

        RunHistoryPage first = await PageAsync($"query={Uri.EscapeDataString(model)}&limit=2");
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.Next);
        Assert.Equal(new RunStateCounts(0, 0, 5, 0, 0), first.Counts);

        // Newer than every run on the first page, so it belongs before it and never shows up further down.
        await RunAsync(machine, DeploymentState.Assigned, 10);

        RunHistoryPage second = await PageAsync($"query={Uri.EscapeDataString(model)}&limit=2&before={first.Next}");
        RunHistoryPage third = await PageAsync($"query={Uri.EscapeDataString(model)}&limit=2&before={second.Next}");

        Assert.Null(second.Counts);
        Assert.Equal(2, second.Items.Count);
        Assert.Single(third.Items);
        Assert.Null(third.Next);

        List<Guid> listed = [.. first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Run.Id)];
        Assert.Equal(runs.Count, listed.Distinct().Count());
        Assert.Equal(runs.ToHashSet(), listed.ToHashSet());
        Assert.Equal(listed.OrderByDescending(i => i), listed);
    }

    [Fact]
    public async Task FiltersByStateAndCountsEveryStateOfTheRest()
    {
        string model = RuleRequests.UniqueModel();
        Guid machine = await MachineAsync(model);
        Guid failed = await RunAsync(machine, DeploymentState.Failed, 1);
        Guid cancelled = await RunAsync(machine, DeploymentState.Cancelled, 2);
        await RunAsync(machine, DeploymentState.Done, 3);
        await RunAsync(machine, DeploymentState.Assigned, 4);

        RunHistoryPage page = await PageAsync($"query={Uri.EscapeDataString(model)}&state=Failed&state=Cancelled");

        Assert.Equal([cancelled, failed], page.Items.Select(i => i.Run.Id));
        Assert.Equal(new RunStateCounts(1, 0, 1, 1, 1), page.Counts);
    }

    [Fact]
    public async Task FiltersByMachineAndBySequence()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        Guid machine = await MachineAsync(RuleRequests.UniqueModel());
        Guid other = await MachineAsync(RuleRequests.UniqueModel());
        Guid ofSequence = await RunAsync(machine, DeploymentState.Done, 1, sequence.Name, sequence.Id);
        Guid otherSequence = await RunAsync(machine, DeploymentState.Done, 2);
        Guid otherMachine = await RunAsync(other, DeploymentState.Done, 3, sequence.Name, sequence.Id);

        Assert.Equal([otherSequence, ofSequence], (await PageAsync($"machineId={machine}")).Items.Select(i => i.Run.Id));
        Assert.Equal([otherMachine, ofSequence], (await PageAsync($"sequenceId={sequence.Id}")).Items.Select(i => i.Run.Id));
        Assert.Equal([ofSequence], (await PageAsync($"sequenceId={sequence.Id}&machineId={machine}")).Items.Select(i => i.Run.Id));
    }

    [Fact]
    public async Task FindsRunsByMachineNameModelSerialMacAndTitle()
    {
        string marker = Guid.NewGuid().ToString("N")[..10];
        Guid named = await MachineAsync(RuleRequests.UniqueModel(), name: $"PC-{marker[..8]}".ToUpperInvariant());
        Guid modelled = await MachineAsync($"Latitude {marker}");
        Guid serial = await MachineAsync(RuleRequests.UniqueModel(), serial: $"SN{marker}");
        Guid byMac = await MachineAsync(RuleRequests.UniqueModel());
        Guid titled = await MachineAsync(RuleRequests.UniqueModel());

        Guid namedRun = await RunAsync(named, DeploymentState.Done, 1);
        Guid modelRun = await RunAsync(modelled, DeploymentState.Done, 2);
        Guid serialRun = await RunAsync(serial, DeploymentState.Done, 3);
        Guid macRun = await RunAsync(byMac, DeploymentState.Done, 4);
        Guid titleRun = await RunAsync(titled, DeploymentState.Done, 5, $"Install {marker.ToUpperInvariant()}");

        async Task<IEnumerable<Guid>> FoundAsync(string query) =>
            (await PageAsync($"query={Uri.EscapeDataString(query)}")).Items.Select(i => i.Run.Id);

        Assert.Equal([namedRun], await FoundAsync($"pc-{marker[..8]}"));
        Assert.Equal([modelRun], await FoundAsync($"LATITUDE {marker.ToUpperInvariant()}"));
        Assert.Equal([serialRun], await FoundAsync($"sn{marker}"));
        Assert.Equal([titleRun], await FoundAsync($"install {marker}"));

        // Typed with separators and in lower case, as it is printed on a label.
        string mac = (await application.MachineAsync(byMac)).PrimaryMac;
        string typed = string.Join(':', Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2))).ToLowerInvariant();
        Assert.Equal([macRun], await FoundAsync(typed));
        Assert.Equal([macRun], await FoundAsync(mac[4..]));
    }

    [Fact]
    public async Task ViewersReadTheHistoryAndOthersDoNot()
    {
        string model = RuleRequests.UniqueModel();
        Guid run = await RunAsync(await MachineAsync(model), DeploymentState.Done, 1);
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);

        Assert.Equal([run], (await PageAsync($"query={Uri.EscapeDataString(model)}", viewer)).Items.Select(i => i.Run.Id));

        using HttpClient anonymous = application.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri(History, UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task RefusesACursorItDidNotHandOut()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage response = await administrator.GetAsync($"{History}?before=not-a-cursor");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task KeepsAPageWithinItsLimit()
    {
        string model = RuleRequests.UniqueModel();
        Guid machine = await MachineAsync(model);
        await RunAsync(machine, DeploymentState.Done, 1);
        Guid newest = await RunAsync(machine, DeploymentState.Done, 2);

        RunHistoryPage page = await PageAsync($"query={Uri.EscapeDataString(model)}&limit=0");

        Assert.Equal([newest], page.Items.Select(i => i.Run.Id));
        Assert.NotNull(page.Next);
    }

    // The history route sits beside the ones for a single run, which must keep answering.
    [Fact]
    public async Task TheRoutesOfOneRunStillAnswer()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        DeploymentSummary run = await administrator.AssignedAsync(machine.Id, sequence.Id);

        Assert.Equal(run.Id, (await administrator.RunAsync(run.Id)).Summary.Id);
        Assert.Equal(HttpStatusCode.OK, (await administrator.GetAsync($"{History}/options")).StatusCode);
    }

    // The history upserts a run by its id with every push, from its assignment to its end.
    [Fact]
    public async Task PushesARunWhenItChanges()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        await using LiveListener listener = await LiveListener.StartAsync(application, viewer);
        ChannelReader<RunHistoryItem> runs = listener.Listen<RunHistoryItem>(LiveEvents.RunChanged);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);

        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, sequence.Id, "PC-PUSHED");
        RunHistoryItem pushed = await LiveListener.NextAsync(runs, r => r.Run.Id == assigned.Id);

        Assert.Equal(machine.Id, pushed.MachineId);
        Assert.Equal("PC-PUSHED", pushed.MachineName);
        Assert.Equal(machine.Registration.PrimaryMac, pushed.PrimaryMac);
        Assert.Equal(DeploymentState.Assigned, pushed.Run.State);
        Assert.Equal(sequence.Name, pushed.Run.Title);

        (await administrator.EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();

        Assert.Equal(DeploymentState.Cancelled, (await LiveListener.NextAsync(runs, r => r.Run.Id == assigned.Id)).Run.State);
    }

    // A poll pushes the machine with its run, but the run did not change, so the history hears nothing of it.
    [Fact]
    public async Task APollThatChangesNothingInTheRunPushesNoRun()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<RunHistoryItem> runs = listener.Listen<RunHistoryItem>(LiveEvents.RunChanged);
        ChannelReader<MachineSummary> machines = listener.Listen<MachineSummary>(LiveEvents.MachineChanged);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        DeploymentSummary assigned = await administrator.AssignedAsync(machine.Id, sequence.Id);
        await LiveListener.NextAsync(runs, r => r.Run.Id == assigned.Id);

        // Past the push interval, so a run push from the poll would go out on its own rather than wait.
        await Task.Delay(LiveNotifier.MachinePushInterval, TestContext.Current.CancellationToken);
        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1));
        await machine.NextAsync();
        await LiveListener.NextAsync(machines, m => m.Id == machine.Id && m.LastSeenUtc > DateTimeOffset.UtcNow - TimeSpan.FromSeconds(30));
        await Task.Delay(LiveNotifier.MachinePushInterval, TestContext.Current.CancellationToken);

        (await administrator.EndCurrentAsync(machine.Id)).EnsureSuccessStatusCode();

        Assert.Equal(DeploymentState.Cancelled, (await LiveListener.NextAsync(runs, r => r.Run.Id == assigned.Id)).Run.State);
    }
}

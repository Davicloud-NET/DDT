// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Pxe;
using DDT.Server.Images;
using DDT.Server.LinuxBoot;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// What the list for netboot costs the database: the commands the refresher sends, as the database's log shows them.
public sealed class BootChoiceRefresherReadTests
{
    private const string Executed = "Executed DbCommand";

    // A run's row holds its answers, values, variables and inputs, and a machine's row its facts and its disks.
    [Fact]
    public async Task ReadsOfEachMachineAndEachActiveRunOnlyWhatNetbootAsks()
    {
        using LoggedApplication application = new(("Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command", "Information"));
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(image.Id));
        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application, registration => registration with { Disks = [DeployingMachine.Disk(0)] });
        (await administrator.PostAsync($"/api/machines/{machine.Id}/approve")).EnsureSuccessStatusCode();
        await administrator.AssignedAsync(machine.Id, sequence.Id, "WIN-90");
        BootChoiceRefresher refresher = ActivatorUtilities.CreateInstance<BootChoiceRefresher>(application.Services);
        int before = application.Log.Entries.Count;

        await refresher.RefreshAsync(TestContext.Current.CancellationToken);

        string[] commands = [.. application.Log.Entries.Skip(before).Select(entry => entry.Message).Where(message => message.Contains(Executed, StringComparison.Ordinal))];
        string runs = Assert.Single(commands, command => command.Contains("FROM \"Deployments\"", StringComparison.Ordinal) && command.Contains("EXISTS", StringComparison.Ordinal));
        string machines = Assert.Single(commands, command => command.Contains("FROM \"Machines\"", StringComparison.Ordinal) && !command.Contains("FROM \"Deployments\"", StringComparison.Ordinal));

        Assert.Contains("\"BootEnvironment\"", runs, StringComparison.Ordinal);
        Assert.All(new[] { "Answers", "Values", "Variables", "Inputs", "Title", "Error" }, column => Assert.DoesNotContain($"\"{column}\"", runs, StringComparison.Ordinal));
        Assert.All(new[] { "Facts", "Disks", "Model", "LastSeenUtc" }, column => Assert.DoesNotContain($"\"{column}\"", machines, StringComparison.Ordinal));

        // And it still learns what it has to.
        BootChoices choices = application.Services.GetRequiredService<BootChoices>();
        Assert.Equal(AgentEnvironment.WindowsPE, choices.For(new NetbootMachine(machine.Registration.PrimaryMac, ReadOnlyMemory<byte>.Empty)).Wanted);
    }
}

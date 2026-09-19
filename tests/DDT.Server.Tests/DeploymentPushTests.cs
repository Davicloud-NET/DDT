using System.Security.Cryptography;
using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using DDT.Server.Images;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// The web UI replaces a machine's row with every push, so a push without the deployment would blank the column.
public sealed class DeploymentPushTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static async Task<MachineSummary> PushedAsync(ChannelReader<MachineSummary> pushes, Guid machineId, Func<MachineSummary, bool> expected)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        await foreach (MachineSummary pushed in pushes.ReadAllAsync(timeout.Token))
        {
            if (pushed.Id == machineId && expected(pushed))
            {
                return pushed;
            }
        }

        throw new InvalidOperationException("The hub closed.");
    }

    [Fact]
    public async Task EveryPushCarriesTheMachinesDeployment()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Channel<MachineSummary> pushes = Channel.CreateUnbounded<MachineSummary>();

        await using HubConnection connection = new HubConnectionBuilder()
            .WithUrl(new Uri(application.Server.BaseAddress, "hubs/live"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => application.Server.CreateHandler();
                options.Headers["Cookie"] = administrator.Cookies.GetCookieHeader(application.Server.BaseAddress);
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions = TestJson.Options)
            .Build();

        connection.On<MachineSummary>("machineChanged", machine => pushes.Writer.TryWrite(machine));
        await connection.StartAsync(TestContext.Current.CancellationToken);

        using DeployingMachine machine = await DeployingMachine.RegisterAsync(application, [DeployingMachine.Disk(0)]);
        MachineSummary registered = await PushedAsync(pushes.Reader, machine.Id, m => m.State == MachineState.Pending);

        Assert.Null(registered.Deployment);
        Assert.Equal(1, registered.EligibleDiskCount);
        Assert.Equal("Disk 0: Msft Virtual Disk, 64 GB, SCSI", registered.Disks);

        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        (await administrator.PostAsync($"/api/machines/{machine.Id}/deployments", new AssignImageRequest(image.Id, null))).EnsureSuccessStatusCode();
        Guid deployment = (await PushedAsync(pushes.Reader, machine.Id, m => m.Deployment?.State == DeploymentState.Assigned)).Deployment!.Id;

        // A poll after a while records last seen and pushes: the deployment stays in the row.
        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1));
        await machine.NextAsync();
        Assert.Equal(deployment, (await PushedAsync(pushes.Reader, machine.Id, m => m.State == MachineState.Approved)).Deployment?.Id);

        await machine.ReportOkAsync(deployment, DeploymentState.Running, DeploymentStep.Download, 35);
        MachineSummary running = await PushedAsync(pushes.Reader, machine.Id, m => m.Deployment?.State == DeploymentState.Running);

        Assert.Equal(MachineState.Deploying, running.State);
        Assert.Equal(DeploymentStep.Download, running.Deployment?.Step);
        Assert.Equal(35, running.Deployment?.Percent);
        Assert.NotNull(running.Deployment?.StartedUtc);

        await machine.ReportOkAsync(deployment, DeploymentState.Failed, DeploymentStep.Apply, 3, "Apply failed.");
        MachineSummary failed = await PushedAsync(pushes.Reader, machine.Id, m => m.State == MachineState.Failed);

        Assert.Equal("Apply failed.", failed.Deployment?.Error);

        // Nothing is active any more: every later push carries the one that ended last.
        await application.ChangeMachineAsync(machine.Id, m => m.LastSeenUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1));
        await machine.NextAsync();
        Assert.Equal(deployment, (await PushedAsync(pushes.Reader, machine.Id, m => m.State == MachineState.Failed && m.Deployment?.State == DeploymentState.Failed)).Deployment?.Id);

        await machine.RegisterAgainAsync();
        Assert.Equal(deployment, (await PushedAsync(pushes.Reader, machine.Id, m => m.State == MachineState.Pending)).Deployment?.Id);

        string operatorName = await application.CreateUserAsync(DdtRoleNames.Operator);
        (await machine.Agent.SignInAsync(machine.Id, machine.Token, new AgentSignInRequest(operatorName, DdtApplication.Password, null))).EnsureSuccessStatusCode();
        Assert.Equal(deployment, (await PushedAsync(pushes.Reader, machine.Id, m => m.State == MachineState.Approved)).Deployment?.Id);

        (await administrator.PostAsync($"/api/machines/{machine.Id}/reject")).EnsureSuccessStatusCode();
        Assert.Equal(deployment, (await PushedAsync(pushes.Reader, machine.Id, m => m.State == MachineState.Rejected)).Deployment?.Id);
    }
}

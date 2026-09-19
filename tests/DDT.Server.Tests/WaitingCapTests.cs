using System.Net;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Server.Tests;

public sealed class WaitingCapTests(WaitingCapApplication application) : IClassFixture<WaitingCapApplication>
{
    private static AgentRegistration NewRegistration() =>
        AgentClient.Registration(Guid.NewGuid().ToString("D"), "02" + Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 5));

    [Fact]
    public async Task CapsMachinesNobodyApprovedPerAddressAndInTotal()
    {
        string lab = TestRemoteAddress.Unique();
        string office = TestRemoteAddress.Unique();

        using RegisteredMachine first = await application.RegisterMachineAsync(lab);
        using RegisteredMachine second = await application.RegisterMachineAsync(lab);
        using AgentClient fromLab = new(application.CreateDefaultClient(), lab);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await fromLab.RegisterAsync(NewRegistration())).StatusCode);

        // A machine the server already knows can always register again.
        Assert.Equal(HttpStatusCode.OK, (await fromLab.RegisterAsync(first.Registration)).StatusCode);

        using RegisteredMachine third = await application.RegisterMachineAsync(office);
        using AgentClient fromOffice = new(application.CreateDefaultClient(), office);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await fromOffice.RegisterAsync(NewRegistration())).StatusCode);

        // An approved machine no longer counts, even after it falls back to waiting.
        SignedInClient administrator = await application.AdministratorAsync();
        (await administrator.PostAsync($"/api/machines/{third.Id}/approve")).EnsureSuccessStatusCode();
        (await fromOffice.RegisterAsync(third.Registration)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await fromOffice.RegisterAsync(NewRegistration())).StatusCode);
    }

    [Fact]
    public async Task MachinesBehindAListedProxyAreCountedAtTheAddressItReports()
    {
        using SettingsApplication proxied = new(
            ("DDT:ForwardedHeaders:KnownProxies", ProxiedApplication.Proxy),
            ("DDT:Machines:MaxWaitingPerAddress", "2"));
        string lab = TestRemoteAddress.Unique();

        using RegisteredMachine first = await proxied.RegisterMachineAsync(ProxiedApplication.Agent(proxied, lab));
        using RegisteredMachine second = await proxied.RegisterMachineAsync(ProxiedApplication.Agent(proxied, lab));
        using AgentClient fromLab = ProxiedApplication.Agent(proxied, lab);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await fromLab.RegisterAsync(NewRegistration())).StatusCode);

        using AgentClient fromOffice = ProxiedApplication.Agent(proxied, TestRemoteAddress.Unique());

        Assert.Equal(HttpStatusCode.OK, (await fromOffice.RegisterAsync(NewRegistration())).StatusCode);
    }
}

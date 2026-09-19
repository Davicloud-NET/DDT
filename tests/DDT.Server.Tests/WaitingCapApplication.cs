using Microsoft.AspNetCore.Hosting;

namespace DDT.Server.Tests;

public sealed class WaitingCapApplication : DdtApplication
{
    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("DDT:Machines:MaxWaitingPerAddress", "2");
        builder.UseSetting("DDT:Machines:MaxWaiting", "3");
    }
}

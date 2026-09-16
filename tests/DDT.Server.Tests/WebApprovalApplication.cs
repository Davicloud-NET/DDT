using Microsoft.AspNetCore.Hosting;

namespace DDT.Server.Tests;

public sealed class WebApprovalApplication : DdtApplication
{
    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("DDT:Machines:RequireWebApproval", "true");
    }
}

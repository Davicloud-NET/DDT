using Microsoft.AspNetCore.Hosting;

namespace DDT.Server.Tests;

// The framework's own switch, which ASPNETCORE_FORWARDEDHEADERS_ENABLED sets, with no proxy in DDT's settings.
public sealed class ForwardedHeadersSwitchApplication : DdtApplication
{
    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ForwardedHeaders_Enabled", "true");
    }
}

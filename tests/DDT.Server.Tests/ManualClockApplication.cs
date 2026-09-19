using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Tests;

// The server's clock moves only when a test advances it, so a timeout fires without waiting for it.
public sealed class ManualClockApplication : DdtApplication
{
    public ManualTimeProvider Clock { get; } = new();

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    }
}

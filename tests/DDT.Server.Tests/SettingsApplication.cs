using Microsoft.AspNetCore.Hosting;

namespace DDT.Server.Tests;

// A host with extra settings, for a test that needs a configuration of its own.
public class SettingsApplication(params (string Key, string Value)[] settings) : DdtApplication
{
    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach ((string key, string value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}

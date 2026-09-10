using DDT.Server.Configuration;

namespace DDT.Host.Startup;

// Every authentication control depends on TLS: Secure cookies are dropped over plain HTTP, so
// without this check sign in appears to succeed and every later request is anonymous. Checked
// against configuration rather than bound addresses, because Kestrel:Endpoints does not populate
// IServerAddressesFeature and the check has to run before anything starts.
public static class HttpsConfigurationCheck
{
    public static void Validate(IConfiguration configuration, DdtOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.RequireHttps || HasHttpsEndpoint(configuration))
        {
            return;
        }

        throw new InvalidOperationException(
            "DDT:RequireHttps is set but no HTTPS endpoint is configured. Set Kestrel:Endpoints:<name>:Url to an " +
            "https URL, or set DDT:RequireHttps to false when a reverse proxy terminates TLS.");
    }

    private static bool HasHttpsEndpoint(IConfiguration configuration)
    {
        foreach (IConfigurationSection endpoint in configuration.GetSection("Kestrel:Endpoints").GetChildren())
        {
            if (IsHttps(endpoint["Url"]))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(configuration["ASPNETCORE_HTTPS_PORTS"]))
        {
            return true;
        }

        string? urls = configuration["Urls"] ?? configuration["ASPNETCORE_URLS"];

        return urls is not null
            && urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(IsHttps);
    }

    private static bool IsHttps(string? url) =>
        url is not null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}

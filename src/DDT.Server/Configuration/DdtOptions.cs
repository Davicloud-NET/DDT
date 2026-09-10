namespace DDT.Server.Configuration;

public sealed class DdtOptions
{
    public const string SectionName = "DDT";

    public string Roles { get; init; } = string.Empty;

    public string StorePath { get; init; } = "/var/lib/ddt";

    public bool RequireHttps { get; init; } = true;

    public HttpsOptions Https { get; init; } = new();
}

namespace DDT.Server.Security;

public sealed class DdtForwardedHeadersOptions
{
    public const string SectionName = "DDT:ForwardedHeaders";

    // Comma separated rather than lists, for the same reason as DDT:Roles.
    public string KnownProxies { get; init; } = string.Empty;

    public string KnownNetworks { get; init; } = string.Empty;
}

namespace DDT.Server.Configuration;

public sealed class DdtOptions
{
    public const string SectionName = "DDT";

    public string Roles { get; init; } = string.Empty;
}

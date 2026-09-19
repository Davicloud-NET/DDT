namespace DDT.Server.Deployments;

// What every deployed machine's answer file carries. Configuration rather than a settings page, like
// DDT:Ldap:BindPassword, and checked at startup by DeploymentOptionsValidation.
public sealed class DeploymentOptions
{
    public const string SectionName = "DDT:Deployment";

    // A Windows id such as W. Europe Standard Time. Unset, Windows picks the zone that matches the locale.
    public string? TimeZone { get; init; }

    // A culture name such as de-DE. Unset, the image's own language.
    public string? Locale { get; init; }

    // An input locale such as 0407:00000407 or de-DE. Unset, the locale.
    public string? Keyboard { get; init; }

    public LocalAdministratorOptions LocalAdministrator { get; init; } = new();

    public DomainOptions Domain { get; init; } = new();
}

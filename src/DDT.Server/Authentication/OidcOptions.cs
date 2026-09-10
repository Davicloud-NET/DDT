namespace DDT.Server.Authentication;

public sealed class OidcOptions
{
    public const string SectionName = "DDT:Oidc";

    public const string SchemeName = "oidc";

    public bool Enabled { get; set; }

    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string DisplayName { get; set; } = "Single sign on";

    public IList<string> Scopes { get; } = ["openid", "profile", "email"];

    // Creates a NEW account keyed on issuer plus subject when an unknown identity signs in.
    // This is not the same as linking an external identity to an existing account by email
    // address, which DDT never does: an issuer that does not verify email addresses would then
    // be able to take over any account by asserting its address.
    public bool AutoProvision { get; set; }

    public string AutoProvisionRole { get; set; } = DdtRoleNames.Viewer;
}

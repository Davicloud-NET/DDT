namespace DDT.Server.Configuration;

public static class DeploymentRoles
{
    private static readonly IReadOnlySet<DeploymentRole> s_default = new HashSet<DeploymentRole> { DeploymentRole.Web };

    public static IReadOnlySet<DeploymentRole> Default => s_default;

    public static IReadOnlySet<DeploymentRole> Parse(string? configured)
    {
        string[] parts = (configured ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return s_default;
        }

        HashSet<DeploymentRole> roles = [];

        foreach (string part in parts)
        {
            DeploymentRole? role = part.ToLowerInvariant() switch
            {
                "web" => DeploymentRole.Web,
                "pxe" => DeploymentRole.Pxe,
                "builder" => DeploymentRole.Builder,
                _ => null,
            };

            if (role is null)
            {
                throw new InvalidOperationException(
                    $"Unknown DDT role '{part}'. Valid roles are {string.Join(", ", Enum.GetNames<DeploymentRole>())}.");
            }

            roles.Add(role.Value);
        }

        return roles;
    }
}

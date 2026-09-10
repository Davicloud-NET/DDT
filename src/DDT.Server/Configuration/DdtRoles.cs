namespace DDT.Server.Configuration;

public static class DdtRoles
{
    private static readonly IReadOnlySet<DdtRole> s_default = new HashSet<DdtRole> { DdtRole.Web };

    public static IReadOnlySet<DdtRole> Default => s_default;

    public static IReadOnlySet<DdtRole> Parse(string? configured)
    {
        string[] parts = (configured ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return s_default;
        }

        HashSet<DdtRole> roles = [];

        foreach (string part in parts)
        {
            DdtRole? role = part.ToLowerInvariant() switch
            {
                "web" => DdtRole.Web,
                "pxe" => DdtRole.Pxe,
                "builder" => DdtRole.Builder,
                _ => null,
            };

            if (role is null)
            {
                throw new InvalidOperationException(
                    $"Unknown DDT role '{part}'. Valid roles are {string.Join(", ", Enum.GetNames<DdtRole>())}.");
            }

            roles.Add(role.Value);
        }

        return roles;
    }
}

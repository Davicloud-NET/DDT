using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Data;

public sealed class DdtRole : IdentityRole<Guid>
{
    public DdtRole()
    {
    }

    public DdtRole(string roleName) : base(roleName)
    {
    }
}

using System.Security.Claims;

namespace DDT.Server.Endpoints;

public static class Principals
{
    public static Guid? UserId(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null;
    }

    public static bool IsMachine(ClaimsPrincipal user, Guid machineId)
    {
        ArgumentNullException.ThrowIfNull(user);

        return UserId(user) == machineId;
    }
}

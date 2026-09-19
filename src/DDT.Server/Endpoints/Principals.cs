using System.Globalization;
using System.Security.Claims;
using DDT.Server.Authentication;
using DDT.Server.Machines;

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

    // The token was checked against an earlier read. Registered again, rejected or stopped since then, the machine
    // belongs to a newer generation whose tokens and content this caller must not receive.
    public static bool HoldsCurrentGeneration(ClaimsPrincipal user, Machine machine)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(machine);

        return machine.TokenGeneration.ToString(CultureInfo.InvariantCulture) == user.FindFirstValue(DdtClaimTypes.TokenGeneration);
    }
}

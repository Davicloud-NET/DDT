using System.Security.Claims;
using System.Text.Encodings.Web;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Authentication;

public sealed class MachineAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    MachineTokenService tokens,
    DdtDbContext database) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;

        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        MachineTokenPayload? payload = tokens.Validate(
            header[BearerPrefix.Length..],
            MachineTokenPurpose.Session);

        if (payload is null)
        {
            return AuthenticateResult.Fail("The machine token is missing, malformed or expired.");
        }

        Machine? machine = await database.Machines
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == payload.MachineId)
            .ConfigureAwait(false);

        if (machine is null || machine.TokenGeneration != payload.TokenGeneration)
        {
            return AuthenticateResult.Fail("The machine token has been superseded.");
        }

        if (machine.State is not (MachineState.Approved or MachineState.Deploying))
        {
            return AuthenticateResult.Fail($"Machine {machine.Id} is {machine.State} and holds no grants.");
        }

        // Binding detects mistakes and casual replay. It is not device identity: an attacker can
        // set both values freely.
        if (!string.Equals(machine.SmbiosUuid, payload.SmbiosUuid, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(machine.PrimaryMac, payload.PrimaryMac, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("The machine token does not match the machine record.");
        }

        ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, machine.Id.ToString()),
                new Claim(DdtClaimTypes.Actor, DdtClaimTypes.MachineActor),
                new Claim(DdtClaimTypes.TokenGeneration, payload.TokenGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ],
            DdtAuthenticationSchemes.Machine);

        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            DdtAuthenticationSchemes.Machine));
    }
}

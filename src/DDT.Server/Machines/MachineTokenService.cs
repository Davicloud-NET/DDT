using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace DDT.Server.Machines;

public sealed class MachineTokenService(IDataProtectionProvider dataProtectionProvider)
{
    private const string PurposeRoot = "DDT.MachineToken";

    public string Issue(Machine machine, MachineTokenPurpose purpose, string? resource = null)
    {
        ArgumentNullException.ThrowIfNull(machine);

        MachineTokenPayload payload = new(
            machine.Id,
            machine.SmbiosUuid,
            machine.PrimaryMac,
            machine.TokenGeneration,
            resource);

        string json = JsonSerializer.Serialize(payload, MachineTokenJsonContext.Default.MachineTokenPayload);

        return Protector(purpose).Protect(json, LifetimeFor(purpose));
    }

    public MachineTokenPayload? Validate(string token, MachineTokenPurpose purpose)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            string json = Protector(purpose).Unprotect(token);

            return JsonSerializer.Deserialize(json, MachineTokenJsonContext.Default.MachineTokenPayload);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ITimeLimitedDataProtector Protector(MachineTokenPurpose purpose) =>
        dataProtectionProvider.CreateProtector(PurposeRoot, purpose.ToString()).ToTimeLimitedDataProtector();

    private static TimeSpan LifetimeFor(MachineTokenPurpose purpose) => purpose switch
    {
        MachineTokenPurpose.Poll => MachineTokenLifetimes.Poll,
        MachineTokenPurpose.Session => MachineTokenLifetimes.Session,
        MachineTokenPurpose.ImageGrant => MachineTokenLifetimes.ImageGrant,
        MachineTokenPurpose.SecretGrant => MachineTokenLifetimes.SecretGrant,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
    };
}

using System.Globalization;
using DDT.Contracts.Agents;

namespace DDT.Server.Machines;

// Everything here was typed by whoever booted boot.wim, so it is bounded and normalised before it
// reaches the database or the web UI.
public static class RegistrationValidator
{
    private const int MaxMacAddresses = 16;
    private const int MaxTextLength = 128;
    private const int MaxVersionLength = 32;

    public static bool TryNormalise(
        AgentRegistration registration,
        out NormalisedRegistration? normalised,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(registration);

        normalised = null;

        if (!Guid.TryParse(registration.SmbiosUuid, out Guid uuid))
        {
            error = "smbiosUuid is not a UUID.";

            return false;
        }

        if (registration.MacAddresses is null || registration.MacAddresses.Count is 0 or > MaxMacAddresses)
        {
            error = $"macAddresses must hold between 1 and {MaxMacAddresses} addresses.";

            return false;
        }

        List<string> macs = [];

        foreach (string mac in registration.MacAddresses)
        {
            if (NormaliseMac(mac) is not { } parsed)
            {
                error = "macAddresses contains a value that is not a MAC address.";

                return false;
            }

            if (!macs.Contains(parsed))
            {
                macs.Add(parsed);
            }
        }

        if (NormaliseMac(registration.PrimaryMac) is not { } primary || !macs.Contains(primary))
        {
            error = "primaryMac must be one of macAddresses.";

            return false;
        }

        normalised = new NormalisedRegistration(
            uuid.ToString("D"),
            primary,
            macs,
            Bound(registration.Manufacturer, MaxTextLength),
            Bound(registration.Model, MaxTextLength),
            Bound(registration.SerialNumber, MaxTextLength),
            Bound(registration.AgentVersion, MaxVersionLength) ?? "unknown",
            registration.ResumeToken);
        error = string.Empty;

        return true;
    }

    private static string? NormaliseMac(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string hex = value.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        return hex.Length == 12 && ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)
            ? hex.ToUpperInvariant()
            : null;
    }

    private static string? Bound(string? value, int maxLength)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

namespace DDT.Core.Unattend;

public static class WindowsTimeZones
{
    // Unattend TimeZone takes a Windows id (tzutil /l). ICU maps every one of them on Windows and Linux alike,
    // but under InvariantGlobalization it knows none, so this belongs on the server and not in the agent.
    public static bool IsValidId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return id.Length > 0 && TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out _);
    }
}

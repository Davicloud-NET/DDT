namespace DDT.Agent.Deployment;

public static class DiskEligibility
{
    // Below what a "32 GB" eMMC exposes, about 31.2 billion bytes. Whether an image fits is checked before each
    // deployment.
    public const long MinimumSizeBytes = 30_000_000_000;

    // Why a disk is left out, or null when DDT may install on it. SD and eMMC stay in when not removable, because
    // cheap laptops have nothing else.
    public static string? ExclusionReason(bool removableMedia, StorageBusType busType, long sizeBytes)
    {
        if (removableMedia)
        {
            return "its media is removable";
        }

        if (busType is StorageBusType.Usb or StorageBusType.Ieee1394 or StorageBusType.IScsi
            or StorageBusType.FileBackedVirtual or StorageBusType.Spaces)
        {
            return $"it is attached through {busType}";
        }

        if (sizeBytes < MinimumSizeBytes)
        {
            return $"it is smaller than {MinimumSizeBytes / 1_000_000_000} GB";
        }

        return null;
    }
}

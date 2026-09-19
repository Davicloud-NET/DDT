namespace DDT.Agent.Deployment;

public static class DriveLetters
{
    // S, W and R as in Microsoft's scripts; a letter already in use is replaced by the highest free one, because
    // Windows PE gives letters to other volumes from C upward. usedMask is GetLogicalDrives: bit 0 is A.
    public static (char System, char Windows, char Recovery) Choose(uint usedMask)
    {
        uint taken = usedMask;
        char system = Take('S', ref taken);
        char windows = Take('W', ref taken);
        char recovery = Take('R', ref taken);

        return (system, windows, recovery);
    }

    private static char Take(char preferred, ref uint taken)
    {
        if (IsFree(preferred, taken))
        {
            taken |= Bit(preferred);

            return preferred;
        }

        for (char letter = 'Z'; letter >= 'D'; letter--)
        {
            if (IsFree(letter, taken))
            {
                taken |= Bit(letter);

                return letter;
            }
        }

        throw new DeploymentStepException("Every drive letter is in use, so the new partitions cannot get one. Remove USB drives and try again.");
    }

    private static bool IsFree(char letter, uint taken) => (taken & Bit(letter)) == 0;

    private static uint Bit(char letter) => 1u << (letter - 'A');
}

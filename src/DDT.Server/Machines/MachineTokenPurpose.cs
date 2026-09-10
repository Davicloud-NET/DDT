namespace DDT.Server.Machines;

// Each purpose gets its own protector, so a poll token issued to a Pending machine cannot be
// presented as a session token, and an image grant cannot be replayed as a secret grant.
public enum MachineTokenPurpose
{
    Poll,
    Session,
    ImageGrant,
    SecretGrant,
}

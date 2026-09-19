namespace DDT.Server.Machines;

// Each purpose gets its own protector, so a poll token issued to a Pending machine cannot be
// presented as a session token, and neither can be replayed as a resume token.
public enum MachineTokenPurpose
{
    Poll,
    Session,
    Resume,
}

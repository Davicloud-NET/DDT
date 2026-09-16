namespace DDT.Agent.Tests;

// Gives each identity once, then keeps giving the last, like a machine whose network comes up late.
internal sealed class SequenceIdentityReader(params MachineIdentity[] identities) : IMachineIdentityReader
{
    private int _reads;

    public MachineIdentity Read() => identities[Math.Min(_reads++, identities.Length - 1)];
}

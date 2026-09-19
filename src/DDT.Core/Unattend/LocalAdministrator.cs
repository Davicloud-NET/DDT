namespace DDT.Core.Unattend;

public sealed record LocalAdministrator(string Name, string Password)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() => $"LocalAdministrator {{ Name = {Name} }}";
}

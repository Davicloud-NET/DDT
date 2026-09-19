namespace DDT.Core.Unattend;

// UserName stays whole (DOMAIN\user or a UPN): splitting a UPN whose suffix is not the AD domain breaks the join.
public sealed record DomainJoin(string Domain, string? OrganizationalUnit, string UserName, string Password)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() =>
        $"DomainJoin {{ Domain = {Domain}, OrganizationalUnit = {OrganizationalUnit}, UserName = {UserName} }}";
}

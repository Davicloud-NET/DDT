namespace DDT.Server.Ldap;

public sealed record LdapIdentity(
    string ImmutableId,
    string DistinguishedName,
    string UserName,
    string? DisplayName,
    string? Email,
    IReadOnlyList<string> GroupDns);

namespace DDT.Server.Ldap;

public interface ILdapAuthenticator
{
    Task<LdapIdentity?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);
}

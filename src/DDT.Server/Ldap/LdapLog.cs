using Microsoft.Extensions.Logging;

namespace DDT.Server.Ldap;

internal static partial class LdapLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "LDAP sign in rejected for {UserName}: empty password would be an unauthenticated bind")]
    public static partial void EmptyPasswordRejected(ILogger logger, string userName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LDAP user {UserName} not found under {BaseDn}")]
    public static partial void UserNotFound(ILogger logger, string userName, string baseDn);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LDAP user filter for {UserName} matched {Count} entries, refusing ambiguous match")]
    public static partial void AmbiguousMatch(ILogger logger, string userName, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "LDAP bind failed for {DistinguishedName} with result {ResultCode}")]
    public static partial void BindFailed(ILogger logger, string distinguishedName, int resultCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "LDAP service bind to {Host}:{Port} failed")]
    public static partial void ServiceBindFailed(ILogger logger, string host, int port, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LDAP entry {DistinguishedName} has no usable {Attribute} value")]
    public static partial void MissingImmutableId(ILogger logger, string distinguishedName, string attribute);
}

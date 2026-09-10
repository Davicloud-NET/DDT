namespace DDT.Host.Logging;

internal static partial class HostLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Active roles: {Roles}")]
    public static partial void ActiveRoles(ILogger logger, string roles);
}

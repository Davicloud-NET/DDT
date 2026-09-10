namespace DDT.Host.Logging;

internal static partial class HostLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Active roles: {Roles}")]
    public static partial void ActiveRoles(ILogger logger, string roles);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Generated a self signed TLS certificate at {Path}. Replace it with your own, or distribute it as a trusted root.")]
    public static partial void GeneratedCertificate(ILogger logger, string path);
}

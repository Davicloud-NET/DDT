using Microsoft.Extensions.Logging;

namespace DDT.Server.Endpoints;

internal static partial class AuthLog
{
    [LoggerMessage(EventId = 200, Level = LogLevel.Information, Message = "Sign in succeeded for {UserName}")]
    public static partial void SignedIn(ILogger logger, string userName);

    [LoggerMessage(EventId = 201, Level = LogLevel.Warning, Message = "Sign in failed for {UserName} from {Address}")]
    public static partial void SignInFailed(ILogger logger, string userName, string address);

    [LoggerMessage(EventId = 202, Level = LogLevel.Warning, Message = "Sign in blocked for locked out account {UserName}")]
    public static partial void LockedOut(ILogger logger, string userName);

    [LoggerMessage(EventId = 203, Level = LogLevel.Information, Message = "Two factor enabled for {UserName}")]
    public static partial void TwoFactorEnabled(ILogger logger, string userName);

    [LoggerMessage(EventId = 204, Level = LogLevel.Warning, Message = "Two factor disabled for {UserName}")]
    public static partial void TwoFactorDisabled(ILogger logger, string userName);
}

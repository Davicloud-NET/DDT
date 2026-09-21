// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

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

    [LoggerMessage(EventId = 205, Level = LogLevel.Information, Message = "{UserName} signed in at machine {MachineId}")]
    public static partial void SignedInAtMachine(ILogger logger, string userName, Guid machineId);

    [LoggerMessage(EventId = 206, Level = LogLevel.Warning, Message = "Sign in at machine {MachineId} failed for {UserName} from {Address}")]
    public static partial void MachineSignInFailed(ILogger logger, Guid machineId, string userName, string address);

    [LoggerMessage(EventId = 207, Level = LogLevel.Warning, Message = "Sign in at machine {MachineId} blocked for locked out account {UserName} from {Address}")]
    public static partial void MachineSignInLockedOut(ILogger logger, Guid machineId, string userName, string address);

    [LoggerMessage(EventId = 208, Level = LogLevel.Warning, Message = "{UserName} may not authorize machines and tried at machine {MachineId} from {Address}")]
    public static partial void MachineSignInNotPermitted(ILogger logger, string userName, Guid machineId, string address);

    [LoggerMessage(EventId = 209, Level = LogLevel.Warning, Message = "No account was created for {UserName} signing in through {Provider}: {Errors}")]
    public static partial void ProvisionFailed(ILogger logger, string provider, string? userName, string errors);
}

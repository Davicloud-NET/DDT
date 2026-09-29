// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Machines;

// Logged under MachineRegistrar's category by every part of a registration.
internal static partial class RegistrationLog
{
    [LoggerMessage(EventId = 410, Level = LogLevel.Warning, Message = "Refused a new machine from {Address}: too many machines nobody approved are waiting")]
    public static partial void TooManyWaiting(ILogger logger, string address);

    [LoggerMessage(EventId = 440, Level = LogLevel.Information, Message = "Machine {MachineId} continued run {RunId} from {Address} in {Environment}")]
    public static partial void Continued(ILogger logger, Guid machineId, Guid runId, string address, AgentEnvironment environment);

    [LoggerMessage(EventId = 441, Level = LogLevel.Warning, Message = "Refused the DDT service in Windows on machine {MachineId} from {Address}: it has no run to continue, so it removes itself")]
    public static partial void NothingToContinue(ILogger logger, Guid? machineId, string address);
}

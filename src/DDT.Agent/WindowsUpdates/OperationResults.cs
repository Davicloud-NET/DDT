// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsUpdates;

// One update's OperationResultCode from wuapi.idl as an outcome. An update an aborted job never reached is aborted.
public static class OperationResults
{
    public const int NotStarted = 0;
    public const int InProgress = 1;
    public const int Succeeded = 2;
    public const int SucceededWithErrors = 3;
    public const int Failed = 4;
    public const int Aborted = 5;

    public static UpdateOutcome Outcome(string id, int code, int hResult, bool restartNeeded) => code switch
    {
        Succeeded or SucceededWithErrors => new UpdateOutcome(id, UpdateResult.Succeeded, RestartNeeded: restartNeeded),
        Failed => new UpdateOutcome(id, UpdateResult.Failed, hResult, restartNeeded),
        _ => new UpdateOutcome(id, UpdateResult.Aborted, RestartNeeded: restartNeeded),
    };
}

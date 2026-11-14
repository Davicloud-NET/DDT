// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// A BitLocker recovery password and its protector, from an Enable BitLocker step. Volume is a drive, such as C:.
public sealed record AgentRecoveryKey(Guid ProtectorId, string Password, string Volume) : AgentRecord
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() => $"AgentRecoveryKey {{ ProtectorId = {ProtectorId}, Volume = {Volume} }}";
}

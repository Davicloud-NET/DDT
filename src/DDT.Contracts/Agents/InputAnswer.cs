// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// An answer to the input Name: Value for every kind but Account, UserName and Password for an Account input. Given at
// the machine or on the web; an Account input's answer is kept for the one run only.
public sealed record InputAnswer(string Name, string? Value, string? UserName = null, string? Password = null)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() => $"InputAnswer {{ Name = {Name}, Value = {Value}, UserName = {UserName} }}";
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// An answer to the input called Name. An Account input uses UserName and Password, and every other kind uses Value.
// Given at the machine or on the web. An Account input's answer is only kept for that one run.
public sealed record InputAnswer(string Name, string? Value, string? UserName = null, string? Password = null)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() => $"InputAnswer {{ Name = {Name}, Value = {Value}, UserName = {UserName} }}";
}

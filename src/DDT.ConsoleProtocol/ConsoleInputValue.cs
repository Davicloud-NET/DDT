// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The answer to the input Name: Value, or UserName and Password for an Account input.
public sealed record ConsoleInputValue(string Name, string? Value, string? UserName = null, string? Password = null)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() => $"ConsoleInputValue {{ Name = {Name}, Value = {Value}, UserName = {UserName} }}";
}

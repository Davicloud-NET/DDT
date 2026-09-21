// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Unattend;

public sealed record LocalAdministrator(string Name, string Password)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() => $"LocalAdministrator {{ Name = {Name} }}";
}

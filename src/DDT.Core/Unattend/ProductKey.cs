// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.RegularExpressions;

namespace DDT.Core.Unattend;

public sealed partial record ProductKey(string Value)
{
    // A record prints every property by default, and the key must never reach a log.
    public override string ToString() => "ProductKey { set }";

    // Five groups of five letters or digits, as Setup takes it.
    public static bool IsValid(string? value) => value is not null && Shape().IsMatch(value.Trim());

    [GeneratedRegex(@"^[A-Za-z0-9]{5}(-[A-Za-z0-9]{5}){4}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}

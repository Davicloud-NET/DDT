// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Values;

// A value of a run or of a preview and where it came from.
public sealed record ResolvedValue(
    string Name,
    // Null for a secret. The page only shows that it's set.
    string? Value,
    ValueSource Source,
    // The rule, machine role or step that set it, if any.
    Guid? SourceId,
    string? SourceName,
    // A source higher in the order also set it, so this value is shown but not used. The order is input answers, the
    // machine's own values, rules from the top, machine roles, the sequence's defaults and the deployment defaults.
    bool Overridden);

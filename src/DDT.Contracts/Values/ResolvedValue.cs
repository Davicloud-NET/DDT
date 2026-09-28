// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Values;

// A value of a run or of a preview and where it came from. SourceId and SourceName name the rule, machine role or step
// that set it, where one did. Overridden marks a value a source further up the order also set, which is shown but not
// used: input answers, then the machine's own values, rules from the top, machine roles, the sequence's defaults and
// the deployment defaults. Value is null for a secret, which shows only that it is set.
public sealed record ResolvedValue(
    string Name,
    string? Value,
    ValueSource Source,
    Guid? SourceId,
    string? SourceName,
    bool Overridden);

public enum ValueSource
{
    Input,
    Machine,
    Rule,
    Role,
    SequenceDefault,
    DeploymentDefault,
    Fact,
    Step,
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// A value the sequence uses, such as ComputerName or Office.
public sealed record VariableDeclaration
{
    public required string Name { get; init; }

    // A template, used as the last resort when the run starts. An input's answer, the machine's own value, and the
    // values of rules and machine roles all come first.
    public string? Default { get; init; }

    public string? Description { get; init; }

    // Only when this is set may a Set variable step or a script's output change the value during the run.
    public bool SetBySteps { get; init; }
}

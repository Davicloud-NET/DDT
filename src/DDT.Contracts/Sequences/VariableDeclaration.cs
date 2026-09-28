// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// A value the sequence uses, such as ComputerName or Office.
public sealed record VariableDeclaration
{
    public required string Name { get; init; }

    // A template, the last choice when the run starts: an input's answer, the machine's own value, the rules' and the
    // machine roles' come first.
    public string? Default { get; init; }

    public string? Description { get; init; }

    // Only then may a Set variable step or a script's output change the value while the run goes on.
    public bool SetBySteps { get; init; }
}

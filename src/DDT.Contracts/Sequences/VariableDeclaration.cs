// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// A value the sequence uses, such as ComputerName or Office. Its value is worked out when the run starts: an input's
// answer, the machine's own, the rules', the machine roles', then Default, a template. Only a variable with SetBySteps
// may be changed by a Set variable step or a script's output while the run goes on.
public sealed record VariableDeclaration
{
    public required string Name { get; init; }

    public string? Default { get; init; }

    public string? Description { get; init; }

    public bool SetBySteps { get; init; }
}

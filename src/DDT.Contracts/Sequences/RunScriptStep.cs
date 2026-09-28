// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

public sealed record RunScriptStep : SequenceStep
{
    public SequencePhase Phase { get; init; }

    public ScriptInterpreter Interpreter { get; init; }

    public required string Script { get; init; }

    // A Files package that is extracted and becomes the script's working directory.
    public Guid? PackageId { get; init; }

    public int TimeoutMinutes { get; init; } = 60;

    public IReadOnlyList<int> SuccessExitCodes { get; init; } = [0];

    public IReadOnlyList<int> RebootExitCodes { get; init; } = [3010];

    // Version 3, Windows phase only: the script runs as this account instead of SYSTEM, and never sees its password.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AccountReference? RunAs { get; init; }

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => Phase;

    [JsonIgnore]
    public override int MinimumVersion => RunAs is null ? 1 : 3;
}

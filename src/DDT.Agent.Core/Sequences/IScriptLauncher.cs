// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// How the system runs a sequence author's script.
public interface IScriptLauncher
{
    // What the step's script needs and this system lacks, as a sentence for the operator. Null when it can run.
    string? Missing(RunScriptStep step, SequencePhase phase);

    // Writes the script into directory, which exists, and returns what runs it.
    Task<ScriptLaunch> WriteAsync(RunScriptStep step, string directory, CancellationToken cancellationToken);

    // Called once the step's package is unpacked into package, for what the system's programs need of their files.
    void Unpacked(string package)
    {
    }
}

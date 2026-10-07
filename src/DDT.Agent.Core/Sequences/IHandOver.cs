// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// Hands a run over to the installed system: stages what goes on with it there, and makes the disk start next.
public interface IHandOver
{
    // Internal, like the run and its boot order.
    internal Task HandOverAsync(SequenceRun run, RunBootOrder bootOrder, CancellationToken cancellationToken);
}

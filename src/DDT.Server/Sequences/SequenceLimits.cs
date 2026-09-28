// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Sequences;

// What the server stores at all, so an editor keeps a draft with problems; SequenceValidator's tighter limits decide what
// runs. Nodes, containers included, are twice what the validator runs, and a run reports a row for each.
public static class SequenceLimits
{
    public const int MaxNameLength = 128;

    public const int MaxDescriptionLength = 1024;

    public const int MaxStoredNodes = 400;

    public const int MaxDefinitionBytes = 1024 * 1024;

    public const int MaxRequestBytes = 2 * 1024 * 1024;
}

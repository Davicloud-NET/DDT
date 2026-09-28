// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Sequences;

// What the server stores at all. A document within these limits is saved even when it has problems, so an editor
// can keep a draft; SequenceValidator's tighter limits decide whether it runs. Nodes are the steps and the groups, IFs
// and repeats that hold them: twice the nodes the validator runs, as it was twice the steps when a sequence was a list,
// and a run reports a row for each. A request holds the document and its name, written less tightly.
public static class SequenceLimits
{
    public const int MaxNameLength = 128;

    public const int MaxDescriptionLength = 1024;

    public const int MaxStoredNodes = 400;

    public const int MaxDefinitionBytes = 1024 * 1024;

    public const int MaxRequestBytes = 2 * 1024 * 1024;
}

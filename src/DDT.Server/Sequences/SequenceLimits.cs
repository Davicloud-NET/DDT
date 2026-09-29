// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Sequences;

// Limits for what the server stores at all, so an editor keeps a draft even with problems. SequenceValidator's tighter
// limits decide what runs. The node limit, containers included, is twice what the validator runs. A run reports a row
// for each node.
public static class SequenceLimits
{
    public const int MaxNameLength = 128;

    public const int MaxDescriptionLength = 1024;

    public const int MaxStoredNodes = 400;

    public const int MaxDefinitionBytes = 1024 * 1024;

    public const int MaxRequestBytes = 2 * 1024 * 1024;
}

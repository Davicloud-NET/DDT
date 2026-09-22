// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Sequences;

// What the server stores at all. A document within these limits is saved even when it has problems, so an editor
// can keep a draft; SequenceValidator's tighter limits decide whether it runs.
public static class SequenceLimits
{
    public const int MaxNameLength = 128;

    public const int MaxDescriptionLength = 1024;

    public const int MaxStoredSteps = 200;

    public const int MaxDefinitionBytes = 256 * 1024;

    public const int MaxRequestBytes = 512 * 1024;
}

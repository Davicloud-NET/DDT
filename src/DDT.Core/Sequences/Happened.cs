// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// What a sequence does at most once per run, and what later steps rely on. ImageEveryTime means an image was applied
// by a step that has no conditions itself. Windows needs that.
[Flags]
internal enum Happened
{
    None = 0,
    Partitioned = 1,
    ImageApplied = 2,
    ImageEveryTime = 4,
    UnattendWritten = 8,
    DomainJoined = 16,
    RawImageWritten = 32,
    SeedWritten = 64,
    All = 127,
}

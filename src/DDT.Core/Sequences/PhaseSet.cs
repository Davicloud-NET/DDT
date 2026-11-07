// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

// Phases by role, so one path analysis serves every kind of sequence.
[Flags]
internal enum PhaseSet
{
    None = 0,
    Boot = 1,
    Installed = 2,
}

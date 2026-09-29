// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Sequences;

[Flags]
internal enum PathPhases
{
    None = 0,
    WindowsPE = 1,
    Windows = 2,
    WindowsPEAfterWindows = 4,
}

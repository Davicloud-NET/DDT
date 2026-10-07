// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// What a sequence installs, which decides the boot environment it runs in.
public enum SequenceKind
{
    Windows,
    Linux,
}

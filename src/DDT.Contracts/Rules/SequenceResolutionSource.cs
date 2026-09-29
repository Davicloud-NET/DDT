// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Rules;

// Where a machine's sequence comes from. The first match wins, in this order: an assignment on the web, a choice at
// the machine, then the first rule in the list that matches the machine and chooses a sequence.
public enum SequenceResolutionSource
{
    None,
    Assigned,
    Console,
    // MacRule and ModelRule are retired. The server no longer sends them, but they keep their numbers.
    MacRule,
    ModelRule,
    Rule,
}

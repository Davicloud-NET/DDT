// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Rules;

// Where a machine's sequence comes from, first match first: an assignment on the web, a choice at the machine, the
// first rule of the ordered list that matches the machine and chooses a sequence. MacRule and ModelRule are what the
// assignment rules before the ordered list were; the server no longer says them, and they keep their numbers.
public enum SequenceResolutionSource
{
    None,
    Assigned,
    Console,
    MacRule,
    ModelRule,
    Rule,
}

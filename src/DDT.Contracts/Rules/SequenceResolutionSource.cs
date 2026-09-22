// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Rules;

// Where a machine's sequence comes from, first match first: an assignment on the web, a choice at the machine, a
// rule for one of its MAC addresses, a rule for its model.
public enum SequenceResolutionSource
{
    None,
    Assigned,
    Console,
    MacRule,
    ModelRule,
}

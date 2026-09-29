// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Both comes first because a member missing from the JSON reads as the first value. That way an input without AskAt
// is asked in both places.
public enum InputAsk
{
    Both,
    Web,
    Machine,
}

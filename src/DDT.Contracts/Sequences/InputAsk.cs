// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Both comes first: a member the JSON leaves out reads as the first value, and an input without AskAt is asked in
// both places.
public enum InputAsk
{
    Both,
    Web,
    Machine,
}

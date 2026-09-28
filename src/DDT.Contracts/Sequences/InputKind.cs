// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

public enum InputKind
{
    Text,
    Choice,
    MultiChoice,
    YesNo,

    // A user name and a password.
    Account,
}

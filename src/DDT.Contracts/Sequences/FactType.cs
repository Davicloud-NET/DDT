// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// What kind of value a fact holds, which decides the operators that fit it. A YesNo value is "true" or "false"; a
// Number is written with the invariant culture.
public enum FactType
{
    Text,
    Number,
    YesNo,
    IPv4,
    Mac,
}

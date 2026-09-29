// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// New operators go at the end, so the numbers of the old ones never change. Everything after Contains needs a
// version 3 SequenceDefinition, because an older agent's evaluator treats an unknown operator as false.
public enum ConditionOperator
{
    Equals,
    NotEquals,
    StartsWith,
    Contains,
    NotContains,
    EndsWith,

    // * stands for any text and ? for one character.
    Matches,

    // Value is a list separated by semicolons. The test holds when the machine's value is one of them.
    In,

    // Whether the machine has a value at all. Value is not read.
    Exists,
    NotExists,

    // Compared as numbers.
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,

    // An IPv4 address within a network written as 10.0.0.0/24.
    InSubnet,
}

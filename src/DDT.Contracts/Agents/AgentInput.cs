// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Contracts.Sequences;

namespace DDT.Contracts.Agents;

// An input from the sequence's InputDeclaration, ready for the console at the machine or the web to ask.
public sealed record AgentInput(
    string Name,
    string Label,
    string? Help,
    // An Account input is answered with a user name and a password. A MultiChoice input gets its values separated by
    // semicolons, and every other kind gets a single value.
    InputKind Kind,
    IReadOnlyList<InputChoice> Choices,
    string? Default,
    bool Required,
    int? MaxLength,
    // For an Account input, the domain the account is for, if its destination names one. It tells the person at the
    // machine which account to enter.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Domain = null);

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Contracts.Sequences;

namespace DDT.Contracts.Agents;

// An input from the sequence's InputDeclaration, as the machine's console or the web asks it.
public sealed record AgentInput(
    string Name,
    string Label,
    string? Help,
    // An Account input is answered with a user name and a password, a MultiChoice one with its values separated by
    // semicolons, every other kind with a value.
    InputKind Kind,
    IReadOnlyList<InputChoice> Choices,
    string? Default,
    bool Required,
    int? MaxLength,
    // The domain an Account input's account is for, where its destination names one, so the person at the machine
    // knows which account to give.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Domain = null);

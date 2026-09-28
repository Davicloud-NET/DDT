// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Contracts.Sequences;

namespace DDT.Contracts.Agents;

// An input as the machine's console or the web asks it, from the sequence's InputDeclaration. An Account input is
// answered with a user name and a password; every other kind with a value, a MultiChoice one with its values separated
// by semicolons. Domain is the domain an Account input's account is for, where its destination names one, so the person
// at the machine knows which account to give.
public sealed record AgentInput(
    string Name,
    string Label,
    string? Help,
    InputKind Kind,
    IReadOnlyList<InputChoice> Choices,
    string? Default,
    bool Required,
    int? MaxLength,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Domain = null);

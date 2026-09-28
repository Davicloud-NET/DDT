// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// One field of an InputsQuestion. An Account input is answered with a user name and a password, typed where nobody else
// sees it; a MultiChoice one with its values separated by semicolons, a YesNo one with "true" or "false".
public sealed record ConsoleInput(
    string Name,
    string Label,
    string? Help,
    ConsoleInputKind Kind,
    IReadOnlyList<ConsoleChoice> Choices,
    string? Default,
    bool Required,
    // Bounds a Text answer.
    int? MaxLength,
    string? Error,
    // The domain an Account input's account is for, so the person knows which account to give.
    string? Domain = null);

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The sequence's inputs the machine asks, all on one page: after the pick, or while a run waits at its start. Answer
// with Values, one per input, or with Back for the list of sequences where the question comes after the pick. The
// agent checks the answers; an input's Error says what was wrong with its last answer, and Error what was wrong with
// them all, such as the server not taking them. Answers are never logged.
public sealed record InputsQuestion(string SequenceName, IReadOnlyList<ConsoleInput> Inputs, string? Error) : ConsoleQuestion;

// One field. An Account input is answered with a user name and a password, the password typed where nobody else sees
// it; every other kind with a value, a MultiChoice one with its values separated by semicolons, a YesNo one with "true"
// or "false". Default is the value the field starts with; MaxLength bounds a Text answer.
public sealed record ConsoleInput(
    string Name,
    string Label,
    string? Help,
    ConsoleInputKind Kind,
    IReadOnlyList<ConsoleChoice> Choices,
    string? Default,
    bool Required,
    int? MaxLength,
    string? Error);

public enum ConsoleInputKind
{
    Text,
    Choice,
    MultiChoice,
    YesNo,
    Account,
}

// Label is what the person sees, Value what the answer sends; a null label shows the value.
public sealed record ConsoleChoice(string Value, string? Label);

// The answer to the input Name: Value, or UserName and Password for an Account input.
public sealed record ConsoleInputValue(string Name, string? Value, string? UserName = null, string? Password = null)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() => $"ConsoleInputValue {{ Name = {Name}, Value = {Value}, UserName = {UserName} }}";
}

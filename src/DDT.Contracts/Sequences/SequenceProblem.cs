// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Contracts.Sequences;

// Every problem keeps a sequence from running. Warnings go in a separate list. StepId is null for the whole sequence.
// Field is the camelCase JSON path within the step, such as "conditions[1].value", so an editor can show the problem
// there. Message is in English, and Code and Args carry the same message for a client in the person's language.
public sealed record SequenceProblem(Guid? StepId, string? Field, string Message, string? Code = null, IReadOnlyDictionary<string, object>? Args = null)
{
    public static SequenceProblem From(Guid? stepId, string? field, ServerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new(stepId, field, message.Text, message.Code, message.Args);
    }

    // Equal when they say the same thing at the same place. Code and Args only repeat Message, and a dictionary
    // compares by reference.
    public bool Equals(SequenceProblem? other) =>
        other is not null && StepId == other.StepId && Field == other.Field && Message == other.Message;

    public override int GetHashCode() => HashCode.Combine(StepId, Field, Message);
}

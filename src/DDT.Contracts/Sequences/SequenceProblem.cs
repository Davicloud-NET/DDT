// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Contracts.Sequences;

// Every problem keeps a sequence from running; warnings go in a list of their own. StepId is null for the whole
// sequence, and Field is the camelCase JSON path within the step, such as "conditions[1].value", for an editor to show
// the problem at. Message is English; Code and Args say the same for a client in the person's language.
public sealed record SequenceProblem(Guid? StepId, string? Field, string Message, string? Code = null, IReadOnlyDictionary<string, object>? Args = null)
{
    public static SequenceProblem From(Guid? stepId, string? field, ServerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new(stepId, field, message.Text, message.Code, message.Args);
    }

    // Equal when they say the same at the same place: the code and its values repeat Message, and a dictionary compares
    // only by reference.
    public bool Equals(SequenceProblem? other) =>
        other is not null && StepId == other.StepId && Field == other.Field && Message == other.Message;

    public override int GetHashCode() => HashCode.Combine(StepId, Field, Message);
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Contracts.Sequences;

// StepId is null for a problem of the whole sequence. Field is the camelCase JSON name within the step, such as
// "script" or "conditions[1].value", so an editor can show the problem at the field. Every problem keeps a sequence
// from running, so a warning, such as Windows steps without a local administrator, goes in a list of its own. Message
// is English; Code and Args are the same sentence for a client that says it in the person's language.
//
// Two problems are equal when they say the same at the same place: the code and its values say it again, and a
// dictionary compares only by reference.
public sealed record SequenceProblem(Guid? StepId, string? Field, string Message, string? Code = null, IReadOnlyDictionary<string, object>? Args = null)
{
    public static SequenceProblem From(Guid? stepId, string? field, ServerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new(stepId, field, message.Text, message.Code, message.Args);
    }

    public bool Equals(SequenceProblem? other) =>
        other is not null && StepId == other.StepId && Field == other.Field && Message == other.Message;

    public override int GetHashCode() => HashCode.Combine(StepId, Field, Message);
}

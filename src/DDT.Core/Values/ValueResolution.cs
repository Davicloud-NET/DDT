// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Values;

namespace DDT.Core.Values;

// What ValueResolver worked out. Values holds every value a source set, grouped by name: first the one used, then the
// ones it overrode. Effective holds the values used, by name ignoring case, for the run and its templates; a value that
// could not be worked out is in Values as it was written, and not in Effective. Problems keep a run from starting.
//
// InputDefaults are what the web and the console fill an input's question with, in the order of the inputs, one for
// each input that has one: the value the machine, a rule or a machine role gives its name, with that source, or else the
// input's own Default as a SequenceDefault. Overridden marks one an answer overrides. An input without one is not listed.
public sealed record ValueResolution(
    IReadOnlyList<ResolvedValue> Values,
    IReadOnlyDictionary<string, string> Effective,
    IReadOnlyList<ValueProblem> Problems,
    IReadOnlyList<ResolvedValue> InputDefaults);

// Name is the value or input the problem is about. Two problems are equal when they say the same about the same name:
// the code and its values say it again, and a dictionary compares only by reference.
public sealed record ValueProblem(string Name, ServerMessage Message)
{
    public bool Equals(ValueProblem? other) =>
        other is not null && Name == other.Name && Message.Code == other.Message.Code && Message.Text == other.Message.Text;

    public override int GetHashCode() => HashCode.Combine(Name, Message.Code, Message.Text);
}

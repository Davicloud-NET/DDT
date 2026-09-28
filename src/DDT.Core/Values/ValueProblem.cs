// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Core.Values;

// Name is the value or input the problem is about.
public sealed record ValueProblem(string Name, ServerMessage Message)
{
    // The same said about the same name. The code and text stand in for the values, a dictionary compared by reference.
    public bool Equals(ValueProblem? other) =>
        other is not null && Name == other.Name && Message.Code == other.Message.Code && Message.Text == other.Message.Text;

    public override int GetHashCode() => HashCode.Combine(Name, Message.Code, Message.Text);
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Core.Applications;

// Field is the camelCase path of the request's field, such as dependsOn[2] or detection.path.
public sealed record ApplicationProblem(string Field, ServerMessage Message)
{
    // Compares code and text, because the values are a dictionary and would compare by reference.
    public bool Equals(ApplicationProblem? other) =>
        other is not null && Field == other.Field && Message.Code == other.Message.Code && Message.Text == other.Message.Text;

    public override int GetHashCode() => HashCode.Combine(Field, Message.Code, Message.Text);
}

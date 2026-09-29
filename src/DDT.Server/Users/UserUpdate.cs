// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Users;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Endpoints;

namespace DDT.Server.Users;

// The values the account has after an update. A field the request left out keeps the account's value. Role is null
// when the request doesn't change the role.
internal sealed record UserUpdate(string? DisplayName, string? Email, string? Role)
{
    public static (UserUpdate Update, FieldProblems Problems) Read(UpdateUserRequest request, DdtUser user)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(user);

        FieldProblems problems = new();
        string? displayName = request.DisplayName is null ? user.DisplayName : UserRequestValidation.Clean(request.DisplayName);
        (string? email, ServerMessage? emailProblem) = request.Email is null ? (user.Email, null) : UserRequestValidation.Email(request.Email);
        string? role = request.Role is null ? null : DdtRoleNames.Canonical(request.Role);

        UserRequestValidation.NotTooLong(problems, "displayName", displayName, ServerMessages.UserDisplayNameTooLong);

        if (emailProblem is not null)
        {
            problems.Add("email", emailProblem);
        }

        if (request.Role is not null && role is null)
        {
            problems.Add("role", ServerMessages.UserRole.With());
        }

        return (new UserUpdate(displayName, email, role), problems);
    }

    public static string Change(string field, string? from, string? to) => $"{field}: '{from}' to '{to}'";

    // Describes what changes besides the role, for the audit log.
    public List<string> Changes(DdtUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        List<string> changes = [];

        if (!string.Equals(DisplayName, user.DisplayName, StringComparison.Ordinal))
        {
            changes.Add(Change("displayName", user.DisplayName, DisplayName));
        }

        if (!string.Equals(Email, user.Email, StringComparison.Ordinal))
        {
            changes.Add(Change("email", user.Email, Email));
        }

        return changes;
    }
}

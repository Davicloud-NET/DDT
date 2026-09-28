// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Users;
using DDT.Server.Authentication;
using DDT.Server.Endpoints;

namespace DDT.Server.Users;

internal sealed record NewUser(string UserName, string DisplayName, string? Email, string Role)
{
    // Null with the problems when the request has any.
    public static (NewUser? User, FieldProblems Problems) Read(CreateUserRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        FieldProblems problems = new();
        string? userName = UserRequestValidation.Clean(request.UserName);
        string? displayName = UserRequestValidation.Clean(request.DisplayName);
        (string? email, ServerMessage? emailProblem) = UserRequestValidation.Email(request.Email);
        string? role = DdtRoleNames.Canonical(request.Role);

        UserRequestValidation.Required(problems, "userName", userName, ServerMessages.UserNameEmpty, ServerMessages.UserNameTooLong);
        UserRequestValidation.Required(problems, "displayName", displayName, ServerMessages.UserDisplayNameEmpty, ServerMessages.UserDisplayNameTooLong);

        if (emailProblem is not null)
        {
            problems.Add("email", emailProblem);
        }

        if (role is null)
        {
            problems.Add("role", ServerMessages.UserRole.With());
        }

        return problems.Count > 0 || userName is null || displayName is null || role is null
            ? (null, problems)
            : (new NewUser(userName, displayName, email, role), problems);
    }
}

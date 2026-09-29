// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Users;
using DDT.Server.Endpoints;
using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Users;

// The result of an administrator's change to an account. Password is the one-time password the admin hands over.
internal sealed record UserChange(UserChangeStatus Status)
{
    public static UserChange NotFound { get; } = new(UserChangeStatus.NotFound);

    public UserView? View { get; init; }

    public string? Password { get; init; }

    public FieldProblems? Problems { get; init; }

    public ServerMessage? Refusal { get; init; }

    public IdentityResult? Failure { get; init; }

    public static UserChange Done(UserView view) => new(UserChangeStatus.Done) { View = view };

    public static UserChange Invalid(FieldProblems problems) => new(UserChangeStatus.Invalid) { Problems = problems };

    public static UserChange Refused(ServerMessage refusal) => new(UserChangeStatus.Refused) { Refusal = refusal };

    public static UserChange NotSaved(IdentityResult failure) => new(UserChangeStatus.NotSaved) { Failure = failure };
}

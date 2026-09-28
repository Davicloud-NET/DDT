// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.CompilerServices;
using DDT.Contracts.Messages;
using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Authentication;

// Keeps the ServerMessage next to each error, so a validation problem can carry its code to the web client. The table
// holds the errors weakly, so they're collected together with their result.
public sealed class DdtIdentityErrorDescriber : IdentityErrorDescriber
{
    private static readonly ConditionalWeakTable<IdentityError, ServerMessage> s_messages = [];

    // Returns null for an error that Identity created without this describer. That error only has its English text.
    public static ServerMessage? MessageOf(IdentityError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return s_messages.TryGetValue(error, out ServerMessage? message) ? message : null;
    }

    public override IdentityError DefaultError() => Error(nameof(DefaultError), ServerMessages.IdentityDefaultError.With());

    public override IdentityError ConcurrencyFailure() =>
        Error(nameof(ConcurrencyFailure), ServerMessages.IdentityConcurrencyFailure.With());

    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), ServerMessages.IdentityPasswordMismatch.With());

    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), ServerMessages.IdentityInvalidToken.With());

    public override IdentityError RecoveryCodeRedemptionFailed() =>
        Error(nameof(RecoveryCodeRedemptionFailed), ServerMessages.IdentityRecoveryCodeRedemptionFailed.With());

    public override IdentityError LoginAlreadyAssociated() =>
        Error(nameof(LoginAlreadyAssociated), ServerMessages.IdentityLoginAlreadyAssociated.With());

    public override IdentityError InvalidUserName(string? userName) =>
        Error(nameof(InvalidUserName), ServerMessages.IdentityInvalidUserName.With("name", userName ?? ""));

    public override IdentityError InvalidEmail(string? email) =>
        Error(nameof(InvalidEmail), ServerMessages.IdentityInvalidEmail.With("email", email ?? ""));

    public override IdentityError DuplicateUserName(string userName) =>
        Error(nameof(DuplicateUserName), ServerMessages.IdentityDuplicateUserName.With("name", userName));

    public override IdentityError DuplicateEmail(string email) =>
        Error(nameof(DuplicateEmail), ServerMessages.IdentityDuplicateEmail.With("email", email));

    public override IdentityError InvalidRoleName(string? role) =>
        Error(nameof(InvalidRoleName), ServerMessages.IdentityInvalidRoleName.With("role", role ?? ""));

    public override IdentityError DuplicateRoleName(string role) =>
        Error(nameof(DuplicateRoleName), ServerMessages.IdentityDuplicateRoleName.With("role", role));

    public override IdentityError UserAlreadyHasPassword() =>
        Error(nameof(UserAlreadyHasPassword), ServerMessages.IdentityUserAlreadyHasPassword.With());

    public override IdentityError UserLockoutNotEnabled() =>
        Error(nameof(UserLockoutNotEnabled), ServerMessages.IdentityUserLockoutNotEnabled.With());

    public override IdentityError UserAlreadyInRole(string role) =>
        Error(nameof(UserAlreadyInRole), ServerMessages.IdentityUserAlreadyInRole.With("role", role));

    public override IdentityError UserNotInRole(string role) =>
        Error(nameof(UserNotInRole), ServerMessages.IdentityUserNotInRole.With("role", role));

    public override IdentityError PasswordTooShort(int length) =>
        Error(nameof(PasswordTooShort), ServerMessages.IdentityPasswordTooShort.With("length", length));

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Error(nameof(PasswordRequiresUniqueChars), ServerMessages.IdentityPasswordRequiresUniqueChars.With("count", uniqueChars));

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Error(nameof(PasswordRequiresNonAlphanumeric), ServerMessages.IdentityPasswordRequiresNonAlphanumeric.With());

    public override IdentityError PasswordRequiresDigit() =>
        Error(nameof(PasswordRequiresDigit), ServerMessages.IdentityPasswordRequiresDigit.With());

    public override IdentityError PasswordRequiresLower() =>
        Error(nameof(PasswordRequiresLower), ServerMessages.IdentityPasswordRequiresLower.With());

    public override IdentityError PasswordRequiresUpper() =>
        Error(nameof(PasswordRequiresUpper), ServerMessages.IdentityPasswordRequiresUpper.With());

    private static IdentityError Error(string code, ServerMessage message)
    {
        IdentityError error = new() { Code = code, Description = message.Text };
        s_messages.AddOrUpdate(error, message);

        return error;
    }
}

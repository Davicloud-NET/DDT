// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Mail;
using DDT.Contracts.Messages;
using DDT.Server.Endpoints;

namespace DDT.Server.Users;

internal static class UserRequestValidation
{
    public const int MaxTextLength = 256;

    public static string? Clean(string? value) =>
        value?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim() is { Length: > 0 } text ? text : null;

    // An empty address clears it. An address with a display name, as in "Jane <jane@corp.example>", is refused.
    public static (string? Email, ServerMessage? Problem) Email(string? value)
    {
        string? email = Clean(value);

        if (email is null)
        {
            return (null, null);
        }

        return email.Length <= MaxTextLength && MailAddress.TryCreate(email, out MailAddress? parsed) && parsed.Address == email
            ? (email, null)
            : (null, ServerMessages.UserEmail.With());
    }

    public static void Required(FieldProblems problems, string field, string? value, MessageTemplate empty, MessageTemplate tooLong)
    {
        if (value is null)
        {
            problems.Add(field, empty.With());
        }
        else
        {
            NotTooLong(problems, field, value, tooLong);
        }
    }

    public static void NotTooLong(FieldProblems problems, string field, string? value, MessageTemplate tooLong)
    {
        if (value?.Length > MaxTextLength)
        {
            problems.Add(field, tooLong.With("max", MaxTextLength));
        }
    }
}

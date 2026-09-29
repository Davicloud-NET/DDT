// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DDT.Server.Endpoints;

// Problem details carry the message's code and args next to its English title, so the web client can show a refusal in
// the person's language. A validation problem also adds errorCodes, which holds its errors as codes, field by field.
public static class ServerProblems
{
    public const string CodeExtension = "code";

    public const string ArgsExtension = "args";

    public const string ErrorCodesExtension = "errorCodes";

    public static ProblemHttpResult Problem(
        ServerMessage message,
        int statusCode,
        IDictionary<string, object?>? extensions = null,
        string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        Dictionary<string, object?> all = new(extensions ?? new Dictionary<string, object?>(), StringComparer.Ordinal)
        {
            [CodeExtension] = message.Code,
            [ArgsExtension] = message.Args,
        };

        return TypedResults.Problem(title: message.Text, detail: detail, statusCode: statusCode, extensions: all);
    }

    public static ValidationProblem Validation(string field, ServerMessage message)
    {
        FieldProblems problems = new();
        problems.Add(field, message);

        return problems.ToResult();
    }
}

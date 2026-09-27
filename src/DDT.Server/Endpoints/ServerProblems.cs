// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DDT.Server.Endpoints;

// Problem details that carry the message's code and values beside its English title, so the web can say a refusal in
// the person's language: { "title": "The machine is Rejected.", "code": "machine.inState", "args": { "state": "Rejected" } }.
// A validation problem keeps its English errors and adds errorCodes, the same messages as codes, field by field.
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

// The messages of a validation problem, by the camelCase name of the field each is about.
public sealed class FieldProblems
{
    private readonly Dictionary<string, List<ServerMessage>> _fields = new(StringComparer.Ordinal);

    public int Count => _fields.Count;

    public void Add(string field, ServerMessage message)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(message);

        if (!_fields.TryGetValue(field, out List<ServerMessage>? messages))
        {
            _fields[field] = messages = [];
        }

        messages.Add(message);
    }

    public ValidationProblem ToResult()
    {
        Dictionary<string, string[]> errors = _fields.ToDictionary(
            field => field.Key,
            field => field.Value.Select(message => message.Text).ToArray(),
            StringComparer.Ordinal);
        Dictionary<string, ServerMessage[]> codes = _fields.ToDictionary(field => field.Key, field => field.Value.ToArray(), StringComparer.Ordinal);

        return TypedResults.ValidationProblem(
            errors,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { [ServerProblems.ErrorCodesExtension] = codes });
    }
}

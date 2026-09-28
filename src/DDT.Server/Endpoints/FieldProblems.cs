// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DDT.Server.Endpoints;

// Collects the messages of a validation problem, keyed by the camelCase name of the field each one is about.
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

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;

namespace DDT.Agent;

// The server answered with an error status other than 401. ProblemTitle is the title of its problem details,
// which is a sentence meant for the operator. FieldErrors are a validation problem's errors, the first message for each
// field, such as the answer to an input the server did not take.
public sealed class AgentRequestException : HttpRequestException
{
    public AgentRequestException()
    {
    }

    public AgentRequestException(string message)
        : base(message)
    {
    }

    public AgentRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AgentRequestException(string message, string? problemTitle, HttpStatusCode statusCode)
        : base(message, null, statusCode) => ProblemTitle = problemTitle;

    public AgentRequestException(string message, string? problemTitle, HttpStatusCode statusCode, IReadOnlyDictionary<string, string>? fieldErrors)
        : this(message, problemTitle, statusCode) => FieldErrors = fieldErrors;

    public string? ProblemTitle { get; }

    public IReadOnlyDictionary<string, string>? FieldErrors { get; }
}

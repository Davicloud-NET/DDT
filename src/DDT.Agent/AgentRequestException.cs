using System.Net;

namespace DDT.Agent;

// The server answered with an error status other than 401. ProblemTitle is the title of its problem details,
// which is a sentence meant for the operator.
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

    public string? ProblemTitle { get; }
}

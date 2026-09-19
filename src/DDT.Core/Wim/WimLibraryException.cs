namespace DDT.Core.Wim;

// The message is a sentence for the operator.
public sealed class WimLibraryException : Exception
{
    public WimLibraryException()
    {
    }

    public WimLibraryException(string message)
        : base(message)
    {
    }

    public WimLibraryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public WimLibraryException(string message, int errorCode, string? failingPath)
        : base(message)
    {
        ErrorCode = errorCode;
        FailingPath = failingPath;
    }

    // The wimlib error code, or 0 when the failure did not come from a wimlib call.
    public int ErrorCode { get; }

    // The file wimlib reported through its error callback, when it named one.
    public string? FailingPath { get; }
}

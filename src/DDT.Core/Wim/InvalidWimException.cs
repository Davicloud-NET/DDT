namespace DDT.Core.Wim;

// The message is a sentence for the operator saying why the file was refused.
public sealed class InvalidWimException : Exception
{
    public InvalidWimException()
    {
    }

    public InvalidWimException(string message)
        : base(message)
    {
    }

    public InvalidWimException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

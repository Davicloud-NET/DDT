namespace DDT.Core.Unattend;

public static class ComputerNames
{
    public const int MaxLength = 15;

    public static bool IsValid(string name, out string error)
    {
        ArgumentNullException.ThrowIfNull(name);

        error = FindProblem(name) ?? string.Empty;

        return error.Length == 0;
    }

    // Only the characters a DNS host name may hold, because the name becomes one on a domain. An invalid name fails
    // Windows setup in the specialize pass, after DDT has already reported the deployment as done.
    private static string? FindProblem(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Enter a computer name.";
        }

        if (name.Length > MaxLength)
        {
            return $"A computer name can have at most {MaxLength} characters.";
        }

        if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            return "A computer name can hold only the letters A to Z, digits and hyphens.";
        }

        if (name.All(char.IsAsciiDigit))
        {
            return "A computer name cannot consist of digits only.";
        }

        if (name[0] == '-')
        {
            return "A computer name cannot start with a hyphen.";
        }

        return null;
    }
}

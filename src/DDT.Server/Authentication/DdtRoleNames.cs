namespace DDT.Server.Authentication;

public static class DdtRoleNames
{
    public const string Administrator = "Administrator";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";

    public static IReadOnlyList<string> All { get; } = [Administrator, Operator, Viewer];
}

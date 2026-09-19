namespace DDT.Agent;

public static class AgentBuild
{
    // Only dotnet publish defines DDT_PUBLISHED. RuntimeFeature cannot tell: PublishAot switches dynamic code off
    // in the JIT build as well.
#if DDT_PUBLISHED
    public static bool IsPublished => true;
#else
    public static bool IsPublished => false;
#endif
}

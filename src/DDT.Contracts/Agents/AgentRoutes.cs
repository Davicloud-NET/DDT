namespace DDT.Contracts.Agents;

public static class AgentRoutes
{
    public const string Register = "api/agents/register";

    public static string Next(Guid machineId) => $"api/agents/{machineId:D}/next";

    public static string Log(Guid machineId) => $"api/agents/{machineId:D}/log";

    public static string SignIn(Guid machineId) => $"api/agents/{machineId:D}/sign-in";
}

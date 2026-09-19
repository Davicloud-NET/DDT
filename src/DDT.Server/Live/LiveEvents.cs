namespace DDT.Server.Live;

public static class LiveEvents
{
    public const string MachineChanged = "machineChanged";

    // Carries nothing: clients load the list again, which is simpler than naming every removed machine.
    public const string MachinesRemoved = "machinesRemoved";
}

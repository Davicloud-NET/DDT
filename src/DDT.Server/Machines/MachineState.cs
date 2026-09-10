namespace DDT.Server.Machines;

public enum MachineState
{
    Pending,
    Approved,
    Deploying,
    Done,
    Failed,
    Rejected,
    Retired,
}
